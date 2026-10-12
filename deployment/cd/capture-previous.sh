#!/usr/bin/env bash
# Record the image and revision a service app runs now. Run it before the service's
# migration, so a failure at any later step still has a rollback target.
# Usage: capture-previous.sh KEY
# Writes previous_image, previous_revision and previous_state to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

key="${1:?usage: capture-previous.sh KEY}"
require_values RESOURCE_GROUP
app=$(app_name "$key")

image="" revision="" state=""
if app_exists "$app"; then
  image=$(current_image "$app")
  revision=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
    --query properties.latestRevisionName --output tsv)
  state=$(running_status "$app")
  log "$key currently runs ${image##*/} (revision $revision, $state)."
else
  log "$key has no app yet; this is a first deployment and there is nothing to roll back to."
fi

{
  echo "previous_image=$image"
  echo "previous_revision=$revision"
  echo "previous_state=$state"
} >> "${GITHUB_OUTPUT:-/dev/stdout}"
