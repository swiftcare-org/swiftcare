#!/usr/bin/env bash
# Deploy the gateway after the services it routes to. It learns each service's internal address
# from the Container Apps, so every deployed service app must be running first: the gateway
# smoke checks exercise all of them. A service app that does not exist yet is left out with a
# warning instead of blocking the release.
# Usage: deploy-gateway.sh
# Writes url=https://<gateway fqdn> to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_values RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT GHCR_USERNAME GHCR_TOKEN \
  IMAGE_REPOSITORY DEPLOY_SHA JWT_ISSUER JWT_AUDIENCE JWT_SIGNING_KEY GATEWAY_INTERNAL_SECRET \
  FRONTEND_ORIGIN FRONTEND_WWW_ORIGIN

gateway_entry=$(deployable_json gateway)
gateway_app=$(app_name gateway)
gateway_port=$(field "$gateway_entry" port)
image="$IMAGE_REPOSITORY/$(field "$gateway_entry" image):$DEPLOY_SHA"

settings=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "APPLICATIONINSIGHTS_CONNECTION_STRING=${APPLICATIONINSIGHTS_CONNECTION_STRING:-}"
  "ASPNETCORE_URLS=http://+:$gateway_port"
  "Jwt__SecretKey=secretref:jwt-key"
  "Jwt__Issuer=$JWT_ISSUER"
  "Jwt__Audience=$JWT_AUDIENCE"
  "Gateway__InternalSecret=secretref:gateway-secret"
  "Cors__AllowedOrigins__0=$FRONTEND_ORIGIN"
  "Cors__AllowedOrigins__1=$FRONTEND_WWW_ORIGIN"
)
secrets=("jwt-key=$JWT_SIGNING_KEY" "gateway-secret=$GATEWAY_INTERNAL_SECRET")

# Read from descriptor 3 so az commands in the loop cannot consume the list.
while IFS= read -r -u 3 service_json; do
  key=$(jq -r '.key' <<<"$service_json")
  cluster=$(jq -r '.gateway_cluster' <<<"$service_json")
  destination=$(jq -r '.gateway_destination' <<<"$service_json")
  app=$(app_name "$key")

  if ! app_exists "$app"; then
    warn "$app is not deployed; the gateway keeps its default address for $cluster."
    continue
  fi

  ensure_running "$key"
  fqdn=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
    --query properties.configuration.ingress.fqdn --output tsv)
  [ -n "$fqdn" ] || fail "$app has no internal ingress FQDN"
  settings+=("ReverseProxy__Clusters__${cluster}__Destinations__${destination}__Address=https://$fqdn")
done 3< <(jq -c '.services[]' "$MANIFEST")

if app_exists "$gateway_app"; then
  az containerapp registry set \
    --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
    --server ghcr.io --username "$GHCR_USERNAME" --password "$GHCR_TOKEN" \
    --only-show-errors >/dev/null
  az containerapp secret set \
    --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
    --secrets "${secrets[@]}" \
    --only-show-errors >/dev/null
  az containerapp update \
    --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
    --image "$image" --cpu 0.25 --memory 0.5Gi \
    --min-replicas 1 --max-replicas 1 \
    --set-env-vars "${settings[@]}" \
    --only-show-errors >/dev/null
else
  az containerapp create \
    --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
    --environment "$(environment_ref)" \
    --image "$image" \
    --registry-server ghcr.io \
    --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_TOKEN" \
    --ingress external --target-port "$gateway_port" \
    --cpu 0.25 --memory 0.5Gi \
    --min-replicas 1 --max-replicas 1 \
    --secrets "${secrets[@]}" \
    --env-vars "${settings[@]}" \
    --only-show-errors >/dev/null
fi

az containerapp ingress enable \
  --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
  --type external --target-port "$gateway_port" --transport auto \
  --only-show-errors >/dev/null
az containerapp revision set-mode \
  --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
  --mode single --only-show-errors >/dev/null

start_app "$gateway_app"
wait_ready "$gateway_app" "$gateway_port" true "$image"

fqdn=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
  --query properties.configuration.ingress.fqdn --output tsv)
[ -n "$fqdn" ] || fail "Gateway has no public ingress FQDN"
echo "url=https://$fqdn" >> "${GITHUB_OUTPUT:-/dev/stdout}"
log "Gateway runs ${image##*/} at https://$fqdn"
