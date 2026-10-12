#!/usr/bin/env bash
# Make the gateway path available for a service's smoke checks.
# Smoke checks go through the gateway and sign in through AuthService, so both must be running.
# Starting a stopped app does not change its image. When the gateway has never been deployed
# (a fresh environment), or the running gateway does not yet know this service's address (the
# service is being deployed for the first time), the checks are skipped here and the gateway job,
# which receives every address, runs all of them.
# Run it before the service changes, so a missing prerequisite fails with nothing to roll back.
# AuthService is not required when KEY is auth itself: its first deployment creates it.
# Usage: prepare-smoke.sh KEY
# Writes skip=true|false and gateway_url to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

key="${1:?usage: prepare-smoke.sh KEY}"
require_values RESOURCE_GROUP
gateway_app=$(app_name gateway)

if ! app_exists "$gateway_app"; then
  warn "The gateway is not deployed yet, so service smoke checks are skipped; the gateway job runs them."
  echo "skip=true" >> "${GITHUB_OUTPUT:-/dev/stdout}"
  exit 0
fi

# The running gateway routes only to addresses it was configured with. A service deployed for the
# first time has none, so a check through this gateway can only return 502.
entry=$(deployable_json "$key")
cluster=$(field "$entry" gateway_cluster)
destination=$(field "$entry" gateway_destination)
if [ -n "$cluster" ]; then
  setting="ReverseProxy__Clusters__${cluster}__Destinations__${destination}__Address"
  known=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
    --query "properties.template.containers[0].env[].name" --output tsv 2>/dev/null | tr -d '\r' || true)
  if ! grep -qx "$setting" <<<"$known"; then
    warn "The running gateway has no address for $key yet, so its smoke check is skipped; the gateway job runs it after learning the address."
    echo "skip=true" >> "${GITHUB_OUTPUT:-/dev/stdout}"
    exit 0
  fi
fi

if [ "$key" != "auth" ]; then ensure_running auth; fi
ensure_running gateway

fqdn=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$gateway_app" \
  --query properties.configuration.ingress.fqdn --output tsv)
[ -n "$fqdn" ] || fail "Gateway has no public ingress FQDN"
{
  echo "skip=false"
  echo "gateway_url=https://$fqdn"
} >> "${GITHUB_OUTPUT:-/dev/stdout}"
