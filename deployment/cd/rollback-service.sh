#!/usr/bin/env bash
# Return one app to the image it ran before this release and wait until it is ready again.
# Only the named app changes. Schemas, data, secrets and settings are left as they are.
# Usage: rollback-service.sh KEY PREVIOUS_IMAGE
# Writes rollback=done|unavailable|same to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

key="${1:?usage: rollback-service.sh KEY PREVIOUS_IMAGE}"
previous_image="${2:-}"
require_values RESOURCE_GROUP

entry=$(deployable_json "$key")
app=$(app_name "$key")
port=$(field "$entry" port)
external=false
[ "$key" = "gateway" ] && external=true

if [ -z "$previous_image" ]; then
  warn "No previous image was recorded for $app, so there is nothing to roll back to. It stays as deployed."
  echo "rollback=unavailable" >> "${GITHUB_OUTPUT:-/dev/stdout}"
  exit 0
fi

# The app ran this image before the release (for example release=all on an unchanged app), so
# restoring it would change nothing. Report that instead of a rollback.
if [ "$(current_image "$app")" = "$previous_image" ]; then
  warn "$app already ran ${previous_image##*/} before this release, so there is nothing to roll back to."
  echo "rollback=same" >> "${GITHUB_OUTPUT:-/dev/stdout}"
  exit 0
fi

log "Rolling back $app to ${previous_image##*/}"
az containerapp update \
  --resource-group "$RESOURCE_GROUP" --name "$app" \
  --image "$previous_image" \
  --only-show-errors >/dev/null

start_app "$app"
wait_ready "$app" "$port" "$external" "$previous_image"
echo "rollback=done" >> "${GITHUB_OUTPUT:-/dev/stdout}"
log "$app is back on ${previous_image##*/}. Other apps were not touched."
