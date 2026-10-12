#!/usr/bin/env bash
# Create or update one service's Container App from the release manifest, start it when it is
# stopped, and wait until the new revision is ready with internal ingress.
# Usage: deploy-service.sh KEY
# Environment: RESOURCE_GROUP, CONTAINERAPPS_ENVIRONMENT, MYSQL_FQDN, GHCR_USERNAME,
# GHCR_TOKEN, IMAGE_REPOSITORY, DEPLOY_SHA, SERVICE_DB_USER, SERVICE_DB_PASSWORD and every
# value the manifest entry references (JWT, Kafka and gateway settings).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

key="${1:?usage: deploy-service.sh KEY}"
entry=$(deployable_json "$key")
[ "$key" != "gateway" ] || fail "The gateway is deployed by deploy-gateway.sh"

require_values RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT MYSQL_FQDN GHCR_USERNAME GHCR_TOKEN \
  IMAGE_REPOSITORY DEPLOY_SHA SERVICE_DB_USER SERVICE_DB_PASSWORD
mapfile -t needed < <(referenced_names "$entry")
require_values "${needed[@]}"

app=$(app_name "$key")
name=$(field "$entry" name)
port=$(field "$entry" port)
db_secret=$(field "$entry" db_secret)
setting=$(field "$entry" connection_setting)
image="$IMAGE_REPOSITORY/$(field "$entry" image):$DEPLOY_SHA"
connection=$(connection_string "$(field "$entry" database)" "$SERVICE_DB_USER" "$SERVICE_DB_PASSWORD")

# Container App secrets: the scoped connection string first, then shared and service-specific ones.
secrets=("$db_secret=$connection")
while IFS=$'\t' read -r secret_name source; do
  [ -n "$secret_name" ] || continue
  secrets+=("$secret_name=${!source}")
done < <(jq -r --slurpfile manifest "$MANIFEST" \
  '($manifest[0].common.secrets + (.extra_secrets // []))[] | [.name, .source] | @tsv' <<<"$entry")

# Settings: fixed values, the URL the container listens on, secret references and extras.
settings=("ASPNETCORE_URLS=http://+:$port" "$setting=secretref:$db_secret")
while IFS= read -r template; do
  [ -n "$template" ] || continue
  settings+=("$(expand "$template")")
done < <(jq -r --slurpfile manifest "$MANIFEST" \
  '($manifest[0].common.env + (.extra_env // []))[]' <<<"$entry")

if app_exists "$app"; then
  az containerapp registry set \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --server ghcr.io --username "$GHCR_USERNAME" --password "$GHCR_TOKEN" \
    --only-show-errors >/dev/null
  az containerapp secret set \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --secrets "${secrets[@]}" \
    --only-show-errors >/dev/null
  az containerapp update \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --image "$image" --cpu 0.25 --memory 0.5Gi \
    --min-replicas 1 --max-replicas 1 \
    --set-env-vars "${settings[@]}" \
    --only-show-errors >/dev/null
else
  az containerapp create \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --environment "$(environment_ref)" \
    --image "$image" \
    --registry-server ghcr.io \
    --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_TOKEN" \
    --ingress internal --target-port "$port" \
    --cpu 0.25 --memory 0.5Gi \
    --min-replicas 1 --max-replicas 1 \
    --secrets "${secrets[@]}" \
    --env-vars "${settings[@]}" \
    --only-show-errors >/dev/null
fi

az containerapp ingress enable \
  --resource-group "$RESOURCE_GROUP" --name "$app" \
  --type internal --target-port "$port" --transport auto \
  --only-show-errors >/dev/null
az containerapp revision set-mode \
  --resource-group "$RESOURCE_GROUP" --name "$app" \
  --mode single --only-show-errors >/dev/null

start_app "$app"
wait_ready "$app" "$port" false "$image"
log "$name runs ${image##*/} with internal ingress on port $port."
