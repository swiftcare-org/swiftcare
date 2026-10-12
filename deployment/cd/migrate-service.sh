#!/usr/bin/env bash
# Configure and run one service's migration job inside the VNet, then wait for the result.
# Usage: migrate-service.sh KEY
# Environment: RESOURCE_GROUP, CONTAINERAPPS_ENVIRONMENT, MYSQL_FQDN, GHCR_USERNAME,
# GHCR_TOKEN, IMAGE_REPOSITORY, DEPLOY_SHA, SERVICE_MIGRATE_JOB, SERVICE_DB_USER and
# SERVICE_DB_PASSWORD (the last three belong to the selected service).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

key="${1:?usage: migrate-service.sh KEY}"
require_values RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT MYSQL_FQDN GHCR_USERNAME GHCR_TOKEN \
  IMAGE_REPOSITORY DEPLOY_SHA SERVICE_MIGRATE_JOB SERVICE_DB_USER SERVICE_DB_PASSWORD

entry=$(deployable_json "$key")
name=$(field "$entry" name)
job="$SERVICE_MIGRATE_JOB"
secret_name=$(field "$entry" db_secret)
setting=$(field "$entry" connection_setting)
image="$IMAGE_REPOSITORY/$(field "$entry" image):$DEPLOY_SHA"
connection=$(connection_string "$(field "$entry" database)" "$SERVICE_DB_USER" "$SERVICE_DB_PASSWORD")

if az containerapp job show --resource-group "$RESOURCE_GROUP" --name "$job" >/dev/null 2>&1; then
  az containerapp job registry set \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --server ghcr.io --username "$GHCR_USERNAME" --password "$GHCR_TOKEN" \
    --only-show-errors >/dev/null
  az containerapp job secret set \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --secrets "$secret_name=$connection" \
    --only-show-errors >/dev/null
  az containerapp job update \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --image "$image" --args=--migrate \
    --cpu 0.25 --memory 0.5Gi \
    --set-env-vars "$setting=secretref:$secret_name" \
    --only-show-errors >/dev/null
else
  az containerapp job create \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --environment "$(environment_ref)" \
    --trigger-type Manual \
    --replica-timeout 600 --replica-retry-limit 1 \
    --replica-completion-count 1 --parallelism 1 \
    --image "$image" --args=--migrate \
    --cpu 0.25 --memory 0.5Gi \
    --registry-server ghcr.io \
    --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_TOKEN" \
    --secrets "$secret_name=$connection" \
    --env-vars "$setting=secretref:$secret_name" \
    --only-show-errors >/dev/null
fi

execution=$(az containerapp job start \
  --resource-group "$RESOURCE_GROUP" --name "$job" \
  --query name --output tsv)
log "Started $name migration execution $execution"

for attempt in $(seq 1 60); do
  status=$(az containerapp job execution show \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --job-execution-name "$execution" \
    --query properties.status --output tsv 2>/dev/null || true)
  case "$status" in
    Succeeded)
      log "$name migration succeeded."
      echo "migration_execution=$execution" >> "${GITHUB_OUTPUT:-/dev/stdout}"
      exit 0
      ;;
    Failed | Degraded)
      fail "$name migration execution $execution ended with $status"
      ;;
    *)
      log "$name migration status: ${status:-Pending} (attempt $attempt/60)"
      sleep 10
      ;;
  esac
done

fail "$name migration execution $execution timed out after 10 minutes"
