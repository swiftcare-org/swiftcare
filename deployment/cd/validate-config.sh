#!/usr/bin/env bash
# Fail before any change when release configuration is missing or malformed.
# Only names are reported, never values.
# Usage: validate-config.sh plan | service KEY | gateway | frontend
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

mode="${1:?usage: validate-config.sh plan|service KEY|gateway|frontend}"
azure_names=(AZURE_LOCATION AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT)
registry_names=(GHCR_USERNAME GHCR_TOKEN)

check_origin() {
  local name="$1" value="${!1:-}"
  case "$value" in */) fail "$name must not end with a slash" ;; esac
  case "$value" in https://*) ;; *) fail "$name must be an HTTPS origin" ;; esac
}

check_signing_key() {
  [ "${#JWT_SIGNING_KEY}" -ge 32 ] || fail "JWT_SIGNING_KEY must be at least 32 characters"
}

# check_azure_name NAME VALUE : Container App and job names must be 2 to 32 characters of lower
# case letters, digits and hyphens, start with a letter, end with a letter or digit and contain
# no "--". Azure rejects anything else only when the resource is created, which can be mid-release.
check_azure_name() {
  local name="$1" value="${2:-}"
  [ "${#value}" -le 32 ] || fail "$name is ${#value} characters; Container App and job names allow at most 32"
  [[ "$value" =~ ^[a-z]([a-z0-9-]*[a-z0-9])?$ ]] || fail "$name must be lower case letters, digits and hyphens, start with a letter and end with a letter or digit"
  [[ "$value" != *--* ]] || fail "$name must not contain two hyphens in a row"
}

# app_variables : the Environment variable that names each Container App.
app_variables() { jq -r '(.services[].app_var), .gateway.app_var' "$MANIFEST"; }

# Where the frontend runs: static_web_app (default) or container_app (AZURE_FRONTEND_APP).
frontend_host="${FRONTEND_HOST:-static_web_app}"
case "$frontend_host" in
  static_web_app | container_app) ;;
  *) fail "FRONTEND_HOST must be static_web_app or container_app" ;;
esac

case "$mode" in
  plan)
    mapfile -t apps < <(app_variables)
    require_values "${azure_names[@]}" FRONTEND_ORIGIN "${apps[@]}"
    for variable in "${apps[@]}"; do check_azure_name "$variable" "${!variable}"; done
    if [ "$frontend_host" = container_app ]; then
      require_values AZURE_FRONTEND_APP
      check_azure_name AZURE_FRONTEND_APP "$AZURE_FRONTEND_APP"
    else
      require_values STATIC_WEB_APP_NAME
    fi
    ;;

  service)
    key="${2:?usage: validate-config.sh service KEY}"
    entry=$(deployable_json "$key")
    app_var=$(field "$entry" app_var)
    mapfile -t needed < <(referenced_names "$entry")
    mapfile -t roles < <(jq -r '.smoke_roles[]?' <<<"$entry")
    smoke=()
    for role in "${roles[@]}"; do
      smoke+=("SMOKE_${role}_USERNAME" "SMOKE_${role}_PASSWORD")
    done
    require_values "${azure_names[@]}" "${registry_names[@]}" MYSQL_FQDN SERVICE_MIGRATE_JOB \
      SERVICE_DB_USER SERVICE_DB_PASSWORD "$app_var" AZURE_AUTH_APP AZURE_GATEWAY_APP \
      "${needed[@]}" "${smoke[@]}"
    if [ -n "${JWT_SIGNING_KEY:-}" ]; then check_signing_key; fi
    check_azure_name "$app_var" "${!app_var}"
    check_azure_name "the migration job name" "$SERVICE_MIGRATE_JOB"
    ;;

  gateway)
    mapfile -t apps < <(app_variables)
    require_values "${azure_names[@]}" "${registry_names[@]}" "${apps[@]}" \
      JWT_ISSUER JWT_AUDIENCE JWT_SIGNING_KEY GATEWAY_INTERNAL_SECRET \
      FRONTEND_ORIGIN FRONTEND_WWW_ORIGIN \
      SMOKE_RECEPTIONIST_USERNAME SMOKE_RECEPTIONIST_PASSWORD SMOKE_DOCTOR_USERNAME SMOKE_DOCTOR_PASSWORD
    check_signing_key
    while IFS= read -r variable; do check_azure_name "$variable" "${!variable}"; done < <(jq -r '(.services[].app_var), .gateway.app_var' "$MANIFEST")
    check_origin FRONTEND_ORIGIN
    check_origin FRONTEND_WWW_ORIGIN
    [ "$FRONTEND_ORIGIN" != "$FRONTEND_WWW_ORIGIN" ] || fail "FRONTEND_ORIGIN and FRONTEND_WWW_ORIGIN must be different origins"
    ;;

  frontend)
    require_values GATEWAY_ORIGIN FRONTEND_ORIGIN
    if [ "$frontend_host" = container_app ]; then
      # The development access policy (dev_team sign-in) exists only on Static Web Apps.
      [ "${TARGET:-}" != development ] ||
        fail "Development needs Static Web Apps for its dev_team access policy; use FRONTEND_HOST=static_web_app"
      require_values "${azure_names[@]}" "${registry_names[@]}" IMAGE_REPOSITORY AZURE_FRONTEND_APP
      check_azure_name AZURE_FRONTEND_APP "$AZURE_FRONTEND_APP"
    else
      require_values AZURE_STATIC_WEB_APPS_API_TOKEN
    fi
    check_origin GATEWAY_ORIGIN
    check_origin FRONTEND_ORIGIN
    ;;

  *)
    fail "Unknown mode '$mode'; use plan, service KEY, gateway or frontend"
    ;;
esac

log "Configuration for $mode is complete."
