#!/usr/bin/env bash
# Deploy the frontend image (frontend/Dockerfile) to its Container App, for environments without
# Static Web Apps (FRONTEND_HOST=container_app). The app has public ingress, scales to zero when
# idle and stays outside the window guard, so the site remains reachable between demo windows at
# almost no cost. The release is confirmed by the commit the new revision publishes in
# /version.json on its Azure address.
# Usage: deploy-frontend.sh
# Writes url=https://<frontend fqdn> to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_values RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT GHCR_USERNAME GHCR_TOKEN \
  IMAGE_REPOSITORY DEPLOY_SHA AZURE_FRONTEND_APP

app="$AZURE_FRONTEND_APP"
port=8080
image="$IMAGE_REPOSITORY/swiftcare-frontend:$DEPLOY_SHA"
# One replica handles a demo; a second absorbs a burst. Zero when nobody is using the site.
scale=(--min-replicas 0 --max-replicas 2
  --scale-rule-name http-requests --scale-rule-type http --scale-rule-http-concurrency 50)

if app_exists "$app"; then
  az containerapp registry set \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --server ghcr.io --username "$GHCR_USERNAME" --password "$GHCR_TOKEN" \
    --only-show-errors >/dev/null
  az containerapp update \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --image "$image" --cpu 0.25 --memory 0.5Gi "${scale[@]}" \
    --only-show-errors >/dev/null
else
  az containerapp create \
    --resource-group "$RESOURCE_GROUP" --name "$app" \
    --environment "$(environment_ref)" \
    --image "$image" \
    --registry-server ghcr.io \
    --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_TOKEN" \
    --ingress external --target-port "$port" \
    --cpu 0.25 --memory 0.5Gi "${scale[@]}" \
    --only-show-errors >/dev/null
fi

az containerapp ingress enable \
  --resource-group "$RESOURCE_GROUP" --name "$app" \
  --type external --target-port "$port" --transport auto \
  --only-show-errors >/dev/null
az containerapp revision set-mode \
  --resource-group "$RESOURCE_GROUP" --name "$app" \
  --mode single --only-show-errors >/dev/null

# A stopped app (for example after a manual stop) serves nothing; scaling to zero is different.
start_app "$app"

fqdn=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
  --query properties.configuration.ingress.fqdn --output tsv)
[ -n "$fqdn" ] || fail "$app has no public ingress FQDN"

# The first request wakes a scaled-to-zero revision; keep asking until the new commit answers.
attempts="${CD_WAIT_ATTEMPTS:-60}" interval="${CD_WAIT_INTERVAL:-5}"
for attempt in $(seq 1 "$attempts"); do
  version=$(curl --silent --fail --max-time 20 "https://$fqdn/version.json?check=$(date +%s)" 2>/dev/null || true)
  served=$(jq -r 'if (.sha | type) == "string" then .sha else empty end' <<<"$version" 2>/dev/null || true)
  if [ "$served" = "$DEPLOY_SHA" ]; then
    echo "url=https://$fqdn" >> "${GITHUB_OUTPUT:-/dev/stdout}"
    log "Frontend runs ${image##*/} at https://$fqdn"
    exit 0
  fi
  log "$app serves ${served:-no commit} on attempt $attempt/$attempts; expected ${DEPLOY_SHA:0:12}"
  sleep "$interval"
done

fail "$app did not serve commit ${DEPLOY_SHA:0:12} in time"
