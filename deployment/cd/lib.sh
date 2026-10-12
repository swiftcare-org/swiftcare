#!/usr/bin/env bash
# Shared helpers for the CD scripts. Source this file; do not run it.
# Each script sets `set -euo pipefail` itself and reads its configuration from the
# environment that the workflow job provides. No helper prints a secret value.

CD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="${MANIFEST:-$CD_DIR/services.json}"

# A literal "&" in a replacement must stay literal in bash 5.2 and later.
shopt -u patsub_replacement 2>/dev/null || true

# jq.exe on Windows ends lines with CRLF; strip the carriage return so values compare equal everywhere.
jq() { command jq "$@" | tr -d $'\r'; }

log() { printf '%s\n' "$*"; }
warn() { printf '::warning::%s\n' "$*"; }
fail() { printf '::error::%s\n' "$*" >&2; exit 1; }

# require_values NAME... : fail naming every empty variable (names only, never values).
require_values() {
  local name missing=()
  for name in "$@"; do
    [ -n "${!name:-}" ] || missing+=("$name")
  done
  [ "${#missing[@]}" -eq 0 ] || fail "Missing required deployment configuration: ${missing[*]}"
}

# deployable_json KEY : print the manifest entry of a service or the gateway.
deployable_json() {
  local key="$1" entry
  entry=$(jq -ec --arg key "$key" \
    '(.services[] | select(.key == $key)) // (.gateway | select(.key == $key))' "$MANIFEST") \
    || fail "Unknown deployable '$key'"
  printf '%s' "$entry"
}

# field JSON NAME : print one top-level field of a manifest entry.
field() { jq -r --arg name "$2" '.[$name] // empty' <<<"$1"; }

# app_name KEY : the Container App name, taken from the Environment variable the manifest names.
app_name() {
  local entry var
  entry=$(deployable_json "$1")
  var=$(field "$entry" app_var)
  require_values "$var"
  printf '%s' "${!var}"
}

# expand TEXT : replace each ${NAME} with that variable's value in a single pass.
expand() {
  local rest="$1" out="" name
  local pattern='^([^$]*)\$\{([A-Za-z_][A-Za-z0-9_]*)\}(.*)$'
  while [[ "$rest" =~ $pattern ]]; do
    name="${BASH_REMATCH[2]}"
    out+="${BASH_REMATCH[1]}${!name:-}"
    rest="${BASH_REMATCH[3]}"
  done
  printf '%s' "$out$rest"
}

# referenced_names JSON... : variable names a service entry needs, excluding optional ones.
referenced_names() {
  jq -r --slurpfile manifest "$MANIFEST" '
    ([.extra_env[]?, $manifest[0].common.env[]?] | map(scan("\\$\\{([A-Za-z0-9_]+)\\}") | .[0])) +
    ([.extra_secrets[]?.source, $manifest[0].common.secrets[]?.source])
    | unique - $manifest[0].common.optional_values | .[]' <<<"$1"
}

# connection_string DATABASE USER PASSWORD : the MySQL connection string; masks it first.
connection_string() {
  local value
  value="Server=${MYSQL_FQDN};Port=3306;Database=$1;User Id=$2;Password=$3;SslMode=Required;"
  printf '::add-mask::%s\n' "$value" >&2
  printf '%s' "$value"
}

# environment_group : the resource group of the Container Apps environment. Production's apps live
# in their own group but join development's environment (one environment per subscription).
environment_group() { printf '%s' "${CONTAINERAPPS_ENVIRONMENT_RESOURCE_GROUP:-$RESOURCE_GROUP}"; }

# environment_ref : what to pass to --environment when creating an app or job. The name is enough
# within the same group; an environment in another group needs its resource ID.
environment_ref() {
  local group
  group=$(environment_group)
  if [ "$group" = "$RESOURCE_GROUP" ]; then
    printf '%s' "$CONTAINERAPPS_ENVIRONMENT"
  else
    az containerapp env show --resource-group "$group" --name "$CONTAINERAPPS_ENVIRONMENT" \
      --query id --output tsv
  fi
}

# app_exists APP
app_exists() {
  az containerapp show --resource-group "$RESOURCE_GROUP" --name "$1" >/dev/null 2>&1
}

# running_status APP : Running, Stopped or empty when the app does not exist.
running_status() {
  az containerapp show --resource-group "$RESOURCE_GROUP" --name "$1" \
    --query properties.runningStatus --output tsv 2>/dev/null || true
}

# current_image APP : the image of the app's template, or empty when the app does not exist.
current_image() {
  az containerapp show --resource-group "$RESOURCE_GROUP" --name "$1" \
    --query "properties.template.containers[0].image" --output tsv 2>/dev/null || true
}

# start_app APP : request a start when the app is Stopped. A concurrent start is not an error.
start_app() {
  local app="$1" app_id
  if [ "$(running_status "$app")" = "Stopped" ]; then
    app_id=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
      --query id --output tsv)
    if az rest --method post \
      --uri "https://management.azure.com${app_id}/start?api-version=2026-01-01" \
      --output none; then
      log "Start requested for $app."
    else
      warn "Start request for $app failed; waiting to see whether another job started it."
    fi
  fi
}

# wait_ready APP PORT EXTERNAL [EXPECTED_IMAGE]
# Wait until the app is Running, its latest revision is ready, ingress matches and, when an
# image is given, the template runs exactly that image. Every deployable uses this routine.
wait_ready() {
  local app="$1" port="$2" external="$3" expected_image="${4:-}"
  local state running latest ready image ingress attempt
  local attempts="${CD_WAIT_ATTEMPTS:-60}" interval="${CD_WAIT_INTERVAL:-5}"

  for attempt in $(seq 1 "$attempts"); do
    # A transient read failure counts as "not ready yet" rather than ending the release.
    state=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
      --query "{running:properties.runningStatus,latest:properties.latestRevisionName,ready:properties.latestReadyRevisionName,image:properties.template.containers[0].image}" \
      --output json 2>/dev/null) || state='{}'
    running=$(jq -r '.running // empty' <<<"$state")
    latest=$(jq -r '.latest // empty' <<<"$state")
    ready=$(jq -r '.ready // empty' <<<"$state")
    image=$(jq -r '.image // empty' <<<"$state")

    if [ "$running" = "Running" ] && [ -n "$latest" ] && [ "$latest" = "$ready" ] &&
      { [ -z "$expected_image" ] || [ "$image" = "$expected_image" ]; }; then
      ingress=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app" \
        --query properties.configuration.ingress --output json)
      if ! jq -e --argjson external "$external" --argjson port "$port" \
        '.external == $external and .targetPort == $port' <<<"$ingress" >/dev/null; then
        fail "$app requires ingress external=$external on port $port"
      fi
      log "$app revision $ready is ready."
      return 0
    fi

    log "$app running=${running:-Unknown} latest=${latest:-Missing} ready=${ready:-Pending}; attempt $attempt/$attempts"
    sleep "$interval"
  done

  fail "$app did not become ready in time"
}

# ensure_running KEY : start a deployed app if stopped and wait for a ready revision.
ensure_running() {
  local entry app port external=false
  entry=$(deployable_json "$1")
  app=$(app_name "$1")
  port=$(field "$entry" port)
  [ "$1" = "gateway" ] && external=true
  app_exists "$app" || fail "$app is not deployed; release it first (release=$1 or release=all)"
  start_app "$app"
  wait_ready "$app" "$port" "$external"
}
