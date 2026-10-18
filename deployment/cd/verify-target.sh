#!/usr/bin/env bash
# Read-only checks that the release targets the intended Azure resources.
# Usage: verify-target.sh azure|data
#   azure : subscription, resource group, Container Apps environment and, unless FRONTEND_HOST is
#           container_app, the Static Web App. The site is expected in STATIC_WEB_APP_LOCATION
#           when set, otherwise in AZURE_LOCATION.
#   data  : MySQL and Kafka (the KAFKA_APP Container App, or the legacy Kafka/ZooKeeper container
#           group when KAFKA_APP is empty); needed only when a backend app is released.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

scope="${1:?usage: verify-target.sh azure|data}"

normalize_location() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr -d ' '
}

# require_location NAME ACTUAL [EXPECTED] : EXPECTED defaults to AZURE_LOCATION.
require_location() {
  local resource_name="$1" actual expected
  actual=$(normalize_location "$2")
  expected=$(normalize_location "${3:-$AZURE_LOCATION}")
  [ "$actual" = "$expected" ] || fail "$resource_name is in $actual; expected $expected"
}

verify_azure() {
  require_values AZURE_LOCATION AZURE_SUBSCRIPTION_ID RESOURCE_GROUP CONTAINERAPPS_ENVIRONMENT

  local actual_subscription group_state environment_state environment_location static_app_location
  actual_subscription=$(az account show --query id --output tsv)
  [ "$actual_subscription" = "$AZURE_SUBSCRIPTION_ID" ] || fail "OIDC selected an unexpected Azure subscription"

  group_state=$(az group show --name "$RESOURCE_GROUP" --query properties.provisioningState --output tsv)
  [ "$group_state" = "Succeeded" ] || fail "Resource group state is $group_state"

  environment_state=$(az containerapp env show --resource-group "$(environment_group)" \
    --name "$CONTAINERAPPS_ENVIRONMENT" --query properties.provisioningState --output tsv)
  [ "$environment_state" = "Succeeded" ] || fail "Container Apps environment state is $environment_state"

  environment_location=$(az containerapp env show --resource-group "$(environment_group)" \
    --name "$CONTAINERAPPS_ENVIRONMENT" --query location --output tsv)
  require_location "Container Apps environment" "$environment_location"

  # A Container App frontend is created by CD in the environment checked above.
  if [ "${FRONTEND_HOST:-static_web_app}" = container_app ]; then
    log "Azure target verified (frontend served from a Container App)."
    return
  fi

  require_values STATIC_WEB_APP_NAME
  static_app_location=$(az staticwebapp show --resource-group "$RESOURCE_GROUP" \
    --name "$STATIC_WEB_APP_NAME" --query location --output tsv)
  # Static Web Apps is offered in fewer regions, so an environment may keep its site elsewhere
  # (production: East Asia while everything else is in Korea Central).
  require_location "Static Web App" "$static_app_location" "${STATIC_WEB_APP_LOCATION:-$AZURE_LOCATION}"
  log "Azure target verified."
}

# Kafka as one KRaft broker in a Container App (Terraform messaging_host = container_app).
verify_kafka_app() {
  local kafka_state kafka_location kafka_ingress
  kafka_state=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$KAFKA_APP" \
    --query properties.runningStatus --output tsv)
  [ "$kafka_state" = "Running" ] ||
    fail "Kafka app $KAFKA_APP is ${kafka_state:-missing}; start it for the window (az containerapp start)"

  kafka_location=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$KAFKA_APP" \
    --query location --output tsv)
  require_location "Kafka app" "$kafka_location"

  kafka_ingress=$(az containerapp show --resource-group "$RESOURCE_GROUP" --name "$KAFKA_APP" \
    --query "properties.configuration.ingress.{external:external,transport:transport}" --output json)
  jq -e '.external == false and ((.transport // "") | ascii_downcase) == "tcp"' <<<"$kafka_ingress" >/dev/null ||
    fail "Kafka app $KAFKA_APP must use internal TCP ingress"

  case "$KAFKA_BOOTSTRAP_SERVERS" in
    "$KAFKA_APP":*) ;;
    *) fail "KAFKA_BOOTSTRAP_SERVERS must name the Kafka app $KAFKA_APP and its port" ;;
  esac
}

# Kafka and ZooKeeper in a container group (Terraform messaging_host = container_group).
verify_kafka_container_group() {
  require_values KAFKA_CONTAINER_GROUP KAFKA_CONTAINER ZOOKEEPER_CONTAINER

  local messaging_state messaging_location messaging_ip_type container_name container_state kafka_zookeeper

  messaging_state=$(az container show --resource-group "$RESOURCE_GROUP" \
    --name "$KAFKA_CONTAINER_GROUP" --query instanceView.state --output tsv)
  [ "$messaging_state" = "Running" ] || fail "Kafka/ZooKeeper container group state is $messaging_state"

  messaging_location=$(az container show --resource-group "$RESOURCE_GROUP" \
    --name "$KAFKA_CONTAINER_GROUP" --query location --output tsv)
  require_location "Kafka/ZooKeeper container group" "$messaging_location"

  messaging_ip_type=$(az container show --resource-group "$RESOURCE_GROUP" \
    --name "$KAFKA_CONTAINER_GROUP" --query ipAddress.type --output tsv)
  [ "$messaging_ip_type" = "Private" ] || fail "Kafka/ZooKeeper must use a private VNet address; found $messaging_ip_type"

  for container_name in "$ZOOKEEPER_CONTAINER" "$KAFKA_CONTAINER"; do
    container_state=$(az container show --resource-group "$RESOURCE_GROUP" \
      --name "$KAFKA_CONTAINER_GROUP" \
      --query "containers[?name=='$container_name'].instanceView.currentState.state | [0]" \
      --output tsv)
    [ "$container_state" = "Running" ] || fail "$container_name container state is ${container_state:-Missing}; expected Running"
  done

  kafka_zookeeper=$(az container show --resource-group "$RESOURCE_GROUP" \
    --name "$KAFKA_CONTAINER_GROUP" \
    --query "containers[?name=='$KAFKA_CONTAINER'] | [0].environmentVariables[?name=='KAFKA_ZOOKEEPER_CONNECT'].value | [0]" \
    --output tsv)
  [ "$kafka_zookeeper" = "localhost:2181" ] ||
    fail "Kafka must use the ZooKeeper container at localhost:2181; found ${kafka_zookeeper:-no setting}"
}

verify_data() {
  require_values AZURE_LOCATION RESOURCE_GROUP MYSQL_SERVER_NAME MYSQL_FQDN KAFKA_BOOTSTRAP_SERVERS

  local mysql_state mysql_location actual_mysql_fqdn public_access

  mysql_state=$(az mysql flexible-server show --resource-group "$RESOURCE_GROUP" \
    --name "$MYSQL_SERVER_NAME" --query state --output tsv)
  [ "$mysql_state" = "Ready" ] || fail "MySQL state is $mysql_state"

  mysql_location=$(az mysql flexible-server show --resource-group "$RESOURCE_GROUP" \
    --name "$MYSQL_SERVER_NAME" --query location --output tsv)
  require_location "MySQL flexible server" "$mysql_location"

  actual_mysql_fqdn=$(az mysql flexible-server show --resource-group "$RESOURCE_GROUP" \
    --name "$MYSQL_SERVER_NAME" --query fullyQualifiedDomainName --output tsv)
  [ "$actual_mysql_fqdn" = "$MYSQL_FQDN" ] || fail "MYSQL_FQDN does not match $MYSQL_SERVER_NAME"

  public_access=$(az mysql flexible-server show --resource-group "$RESOURCE_GROUP" \
    --name "$MYSQL_SERVER_NAME" --query network.publicNetworkAccess --output tsv)
  [ "$public_access" = "Disabled" ] || fail "MySQL public access is $public_access; expected Disabled"

  # KAFKA_APP selects the Container App broker; without it the legacy container group is checked.
  if [ -n "${KAFKA_APP:-}" ]; then verify_kafka_app; else verify_kafka_container_group; fi

  case "$KAFKA_BOOTSTRAP_SERVERS" in
    *localhost* | *127.0.0.1* | *kafka:29092*)
      fail "KAFKA_BOOTSTRAP_SERVERS must be reachable from Container Apps, not a local Docker address"
      ;;
  esac
  log "Data layer verified."
}

case "$scope" in
  azure) verify_azure ;;
  data) verify_data ;;
  *) fail "Unknown scope '$scope'; use azure or data" ;;
esac
