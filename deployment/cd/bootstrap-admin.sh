#!/usr/bin/env bash
# Create the initial administrator through a temporary AuthService job, then delete the job so
# the one-time credentials do not outlive the run. Run it after the Auth migration.
# Environment: RESOURCE_GROUP, CONTAINERAPPS_ENVIRONMENT, MYSQL_FQDN, GHCR_USERNAME, GHCR_TOKEN,
# IMAGE_REPOSITORY, DEPLOY_SHA, AUTH_BOOTSTRAP_JOB, SERVICE_DB_USER, SERVICE_DB_PASSWORD,
# INITIAL_ADMIN_USERNAME, INITIAL_ADMIN_PASSWORD and optionally INITIAL_ADMIN_FULL_NAME.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

require_values RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT MYSQL_FQDN GHCR_USERNAME GHCR_TOKEN \
  IMAGE_REPOSITORY DEPLOY_SHA AUTH_BOOTSTRAP_JOB SERVICE_DB_USER SERVICE_DB_PASSWORD \
  INITIAL_ADMIN_USERNAME INITIAL_ADMIN_PASSWORD

if [ "${#INITIAL_ADMIN_PASSWORD}" -lt 8 ]; then
  fail "INITIAL_ADMIN_PASSWORD must be at least 8 characters"
fi

entry=$(deployable_json auth)
job="$AUTH_BOOTSTRAP_JOB"
setting=$(field "$entry" connection_setting)
image="$IMAGE_REPOSITORY/$(field "$entry" image):$DEPLOY_SHA"
connection=$(connection_string "$(field "$entry" database)" "$SERVICE_DB_USER" "$SERVICE_DB_PASSWORD")

cleanup_bootstrap_job() {
  if az containerapp job show --resource-group "$RESOURCE_GROUP" --name "$job" >/dev/null 2>&1; then
    az containerapp job delete \
      --resource-group "$RESOURCE_GROUP" --name "$job" \
      --yes --only-show-errors
  fi
}

# Delete the one-time credentials even if creation, execution or polling fails.
trap cleanup_bootstrap_job EXIT
cleanup_bootstrap_job

az containerapp job create \
  --resource-group "$RESOURCE_GROUP" --name "$job" \
  --environment "$CONTAINERAPPS_ENVIRONMENT" \
  --trigger-type Manual \
  --replica-timeout 600 --replica-retry-limit 0 \
  --replica-completion-count 1 --parallelism 1 \
  --image "$image" --args=--bootstrap-admin \
  --cpu 0.25 --memory 0.5Gi \
  --registry-server ghcr.io \
  --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_TOKEN" \
  --secrets \
    "auth-db=$connection" \
    "admin-username=$INITIAL_ADMIN_USERNAME" \
    "admin-password=$INITIAL_ADMIN_PASSWORD" \
  --env-vars \
    "$setting=secretref:auth-db" \
    "INITIAL_ADMIN_USERNAME=secretref:admin-username" \
    "INITIAL_ADMIN_PASSWORD=secretref:admin-password" \
    "INITIAL_ADMIN_FULL_NAME=${INITIAL_ADMIN_FULL_NAME:-}" \
  --only-show-errors >/dev/null

execution=$(az containerapp job start \
  --resource-group "$RESOURCE_GROUP" --name "$job" \
  --query name --output tsv)
result=TimedOut

for _ in $(seq 1 60); do
  status=$(az containerapp job execution show \
    --resource-group "$RESOURCE_GROUP" --name "$job" \
    --job-execution-name "$execution" \
    --query properties.status --output tsv 2>/dev/null || true)
  if [ "$status" = "Succeeded" ]; then
    result=Succeeded
    break
  fi
  if [ "$status" = "Failed" ] || [ "$status" = "Degraded" ]; then
    result="$status"
    break
  fi
  sleep 10
done

[ "$result" = "Succeeded" ] || fail "Administrator bootstrap ended with $result"

cleanup_bootstrap_job
trap - EXIT
log "Initial administrator is present. Bootstrap job removed."
