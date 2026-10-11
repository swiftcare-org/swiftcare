#!/usr/bin/env bash
# Read what each deployable runs today, as input for select_services.py. Read-only.
# Usage: read-state.sh OUTPUT_FILE
# Apps: existence, image and running status. Frontend: the commit published in /version.json.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

output="${1:?usage: read-state.sh OUTPUT_FILE}"
require_values RESOURCE_GROUP FRONTEND_ORIGIN

apps='{}'
while IFS= read -r -u 3 key; do
  app=$(app_name "$key")
  if app_exists "$app"; then
    image=$(current_image "$app")
    status=$(running_status "$app")
    apps=$(jq -c --arg key "$key" --arg image "$image" --arg status "$status" \
      '. + {($key): {exists: true, image: $image, running: $status}}' <<<"$apps")
    log "$key: ${image##*/} ($status)"
  else
    apps=$(jq -c --arg key "$key" '. + {($key): {exists: false, image: null, running: null}}' <<<"$apps")
    log "$key: no app"
  fi
done 3< <(jq -r '(.services[].key), .gateway.key' "$MANIFEST")

# The frontend publishes its commit in /version.json. Anything else means "unknown".
frontend_sha=""
version=$(curl --silent --fail --location --max-time 20 \
  "$FRONTEND_ORIGIN/version.json?check=$(date +%s)" 2>/dev/null || true)
if [ -n "$version" ]; then
  frontend_sha=$(jq -r 'if (.sha | type) == "string" then .sha else empty end' <<<"$version" 2>/dev/null || true)
fi
if [[ "$frontend_sha" =~ ^[0-9a-f]{40}$ ]]; then
  log "frontend: ${frontend_sha:0:12}"
else
  frontend_sha=""
  log "frontend: no published commit"
fi

jq -n --argjson apps "$apps" --arg sha "$frontend_sha" \
  '{apps: $apps, frontend: {sha: (if $sha == "" then null else $sha end)}}' > "$output"
