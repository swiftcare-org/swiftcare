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

# app_variables : the Environment variable that names each Container App.
app_variables() { jq -r '(.services[].app_var), .gateway.app_var' "$MANIFEST"; }

case "$mode" in
  plan)
    mapfile -t apps < <(app_variables)
    require_values "${azure_names[@]}" STATIC_WEB_APP_NAME FRONTEND_ORIGIN "${apps[@]}"
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
    ;;

  gateway)
    mapfile -t apps < <(app_variables)
    require_values "${azure_names[@]}" "${registry_names[@]}" "${apps[@]}" \
      JWT_ISSUER JWT_AUDIENCE JWT_SIGNING_KEY GATEWAY_INTERNAL_SECRET \
      FRONTEND_ORIGIN FRONTEND_WWW_ORIGIN \
      SMOKE_RECEPTIONIST_USERNAME SMOKE_RECEPTIONIST_PASSWORD SMOKE_DOCTOR_USERNAME SMOKE_DOCTOR_PASSWORD
    check_signing_key
    check_origin FRONTEND_ORIGIN
    check_origin FRONTEND_WWW_ORIGIN
    [ "$FRONTEND_ORIGIN" != "$FRONTEND_WWW_ORIGIN" ] || fail "FRONTEND_ORIGIN and FRONTEND_WWW_ORIGIN must be different origins"
    ;;

  frontend)
    require_values GATEWAY_ORIGIN FRONTEND_ORIGIN AZURE_STATIC_WEB_APPS_API_TOKEN
    check_origin GATEWAY_ORIGIN
    check_origin FRONTEND_ORIGIN
    ;;

  *)
    fail "Unknown mode '$mode'; use plan, service KEY, gateway or frontend"
    ;;
esac

log "Configuration for $mode is complete."
