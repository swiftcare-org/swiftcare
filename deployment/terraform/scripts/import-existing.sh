#!/usr/bin/env bash
set -euo pipefail

# Git Bash rewrites arguments beginning with / as Windows paths. Azure resource
# IDs must reach Terraform and Azure CLI unchanged.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL="*"

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI is required." >&2
  exit 1
fi

if ! command -v terraform >/dev/null 2>&1; then
  echo "Terraform is required." >&2
  exit 1
fi

: "${SUBSCRIPTION_ID:?Set SUBSCRIPTION_ID before running this script.}"

RESOURCE_GROUP="${RESOURCE_GROUP:-swiftcare-rg}"
PROJECT_NAME="${PROJECT_NAME:-swiftcare}"
# Azure resource names follow NAME_PREFIX (swiftcare-prod for production); database names keep
# PROJECT_NAME, as in locals.tf.
NAME_PREFIX="${NAME_PREFIX:-$PROJECT_NAME}"
MYSQL_SERVER_NAME="${MYSQL_SERVER_NAME:-${NAME_PREFIX}-mysql}"
STATIC_WEB_APP_NAME="${STATIC_WEB_APP_NAME:-${NAME_PREFIX}-web}"
DEPLOYMENT_IDENTITY_NAME="${DEPLOYMENT_IDENTITY_NAME:-${NAME_PREFIX}-github-cd}"
FRONTEND_DOMAIN="${FRONTEND_DOMAIN:-swiftcare.me}"
# Set FRONTEND_WWW_DOMAIN="" for an environment without a second domain (development).
FRONTEND_WWW_DOMAIN="${FRONTEND_WWW_DOMAIN-www.swiftcare.me}"
IMPORT_CUSTOM_DOMAINS="${IMPORT_CUSTOM_DOMAINS:-false}"
# Set IMPORT_STATIC_WEB_APP=false where static_web_app_enabled = false (the production subscription).
IMPORT_STATIC_WEB_APP="${IMPORT_STATIC_WEB_APP:-true}"
IMPORT_MESSAGING="${IMPORT_MESSAGING:-true}"
# MESSAGING_HOST matches the messaging_host variable: container_app (default) or container_group.
MESSAGING_HOST="${MESSAGING_HOST:-container_app}"
# Production joins development's Container Apps environment: set SHARED_RESOURCE_GROUP and
# SHARED_VNET_NAME (swiftcare-rg and swiftcare-vnet) to import the peerings and DNS links instead
# of an environment and its subnet.
SHARED_RESOURCE_GROUP="${SHARED_RESOURCE_GROUP:-}"
SHARED_VNET_NAME="${SHARED_VNET_NAME:-}"

BASE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers"
VNET_ID="${BASE}/Microsoft.Network/virtualNetworks/${NAME_PREFIX}-vnet"
MYSQL_SERVER_ID="${BASE}/Microsoft.DBforMySQL/flexibleServers/${MYSQL_SERVER_NAME}"
MYSQL_DNS_ID="${BASE}/Microsoft.Network/privateDnsZones/${NAME_PREFIX}.private.mysql.database.azure.com"
MESSAGING_DNS_ID="${BASE}/Microsoft.Network/privateDnsZones/${NAME_PREFIX}.internal"
NAT_ID="${BASE}/Microsoft.Network/natGateways/${NAME_PREFIX}-messaging-nat"
PUBLIC_IP_ID="${BASE}/Microsoft.Network/publicIPAddresses/${NAME_PREFIX}-messaging-nat-ip"
IDENTITY_ID="${BASE}/Microsoft.ManagedIdentity/userAssignedIdentities/${DEPLOYMENT_IDENTITY_NAME}"
STATIC_WEB_APP_ID="${BASE}/Microsoft.Web/staticSites/${STATIC_WEB_APP_NAME}"

import_if_missing() {
  local address="$1"
  local resource_id="$2"

  if terraform state show "$address" >/dev/null 2>&1; then
    echo "Already imported: ${address}"
    return
  fi

  echo "Importing ${address}"
  terraform import "$address" "$resource_id"
}

az account set --subscription "$SUBSCRIPTION_ID"

import_if_missing azurerm_resource_group.swiftcare \
  "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}"
import_if_missing azurerm_virtual_network.swiftcare "$VNET_ID"
import_if_missing azurerm_subnet.mysql "${VNET_ID}/subnets/mysql-subnet"
import_if_missing azurerm_subnet.messaging "${VNET_ID}/subnets/messaging-subnet"

import_if_missing azurerm_log_analytics_workspace.swiftcare \
  "${BASE}/Microsoft.OperationalInsights/workspaces/${NAME_PREFIX}-logs"
import_if_missing azurerm_application_insights.swiftcare \
  "${BASE}/Microsoft.Insights/components/${NAME_PREFIX}-appinsights"
if [[ -z "$SHARED_RESOURCE_GROUP" ]]; then
  import_if_missing 'azurerm_subnet.container_apps[0]' "${VNET_ID}/subnets/containerapps-subnet"
  import_if_missing 'azurerm_container_app_environment.swiftcare[0]' \
    "${BASE}/Microsoft.App/managedEnvironments/${NAME_PREFIX}-aca-env"
else
  : "${SHARED_VNET_NAME:?Set SHARED_VNET_NAME with SHARED_RESOURCE_GROUP.}"
  SHARED_VNET_ID="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${SHARED_RESOURCE_GROUP}/providers/Microsoft.Network/virtualNetworks/${SHARED_VNET_NAME}"
  import_if_missing 'azurerm_virtual_network_peering.to_shared[0]' \
    "${VNET_ID}/virtualNetworkPeerings/${NAME_PREFIX}-to-shared"
  import_if_missing 'azurerm_virtual_network_peering.from_shared[0]' \
    "${SHARED_VNET_ID}/virtualNetworkPeerings/shared-to-${NAME_PREFIX}"
fi

import_if_missing azurerm_private_dns_zone.mysql "$MYSQL_DNS_ID"
import_if_missing azurerm_private_dns_zone_virtual_network_link.mysql \
  "${MYSQL_DNS_ID}/virtualNetworkLinks/mysql-vnet-link"
import_if_missing azurerm_mysql_flexible_server.swiftcare "$MYSQL_SERVER_ID"
import_if_missing azurerm_mysql_flexible_database.auth \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_auth"
import_if_missing azurerm_mysql_flexible_database.patient \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_patient"
import_if_missing azurerm_mysql_flexible_database.queue \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_queue"
import_if_missing azurerm_mysql_flexible_database.medical_record \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_medical_record"
import_if_missing azurerm_mysql_flexible_database.prescription \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_prescription"
import_if_missing azurerm_mysql_flexible_database.notification \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_notification"

import_if_missing azurerm_private_dns_zone.messaging "$MESSAGING_DNS_ID"
import_if_missing azurerm_private_dns_zone_virtual_network_link.messaging \
  "${MESSAGING_DNS_ID}/virtualNetworkLinks/messaging-vnet-link"
if [[ -n "$SHARED_RESOURCE_GROUP" ]]; then
  import_if_missing 'azurerm_private_dns_zone_virtual_network_link.mysql_shared[0]' \
    "${MYSQL_DNS_ID}/virtualNetworkLinks/mysql-shared-vnet-link"
  import_if_missing 'azurerm_private_dns_zone_virtual_network_link.messaging_shared[0]' \
    "${MESSAGING_DNS_ID}/virtualNetworkLinks/messaging-shared-vnet-link"
fi

if [[ "$IMPORT_MESSAGING" == "true" && "$MESSAGING_HOST" == "container_app" ]]; then
  import_if_missing 'azurerm_container_app.kafka[0]'     "${BASE}/Microsoft.App/containerApps/${NAME_PREFIX}-kafka"
fi

if [[ "$IMPORT_MESSAGING" == "true" && "$MESSAGING_HOST" == "container_group" ]]; then
  import_if_missing 'azurerm_public_ip.messaging[0]' "$PUBLIC_IP_ID"
  import_if_missing 'azurerm_nat_gateway.messaging[0]' "$NAT_ID"
  import_if_missing 'azurerm_nat_gateway_public_ip_association.messaging[0]' \
    "${NAT_ID}|${PUBLIC_IP_ID}"
  import_if_missing 'azurerm_subnet_nat_gateway_association.messaging[0]' \
    "${VNET_ID}/subnets/messaging-subnet"
  import_if_missing 'azurerm_container_group.messaging[0]' \
    "${BASE}/Microsoft.ContainerInstance/containerGroups/${NAME_PREFIX}-messaging"
  import_if_missing 'azurerm_private_dns_a_record.kafka[0]' \
    "${MESSAGING_DNS_ID}/A/kafka"
fi

# SWC-132 availability monitoring. Importing reads the live enabled flags and recipients;
# the next plan shows any difference from the local availability inputs.
import_if_missing azurerm_monitor_action_group.availability \
  "${BASE}/Microsoft.Insights/actionGroups/${NAME_PREFIX}-availability-team"
for target in frontend gateway; do
  import_if_missing "azurerm_application_insights_standard_web_test.availability[\"${target}\"]" \
    "${BASE}/Microsoft.Insights/webTests/${NAME_PREFIX}-${target}-availability"
  import_if_missing "azurerm_monitor_metric_alert.availability[\"${target}\"]" \
    "${BASE}/Microsoft.Insights/metricAlerts/${NAME_PREFIX}-${target}-availability-failure"
done

BUDGET_NAME="${NAME_PREFIX}-monthly-budget"
if az consumption budget show --resource-group "$RESOURCE_GROUP" --budget-name "$BUDGET_NAME" >/dev/null 2>&1; then
  import_if_missing 'azurerm_consumption_budget_resource_group.monthly[0]' \
    "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.Consumption/budgets/${BUDGET_NAME}"
fi

if [[ "$IMPORT_STATIC_WEB_APP" == "true" ]]; then
  import_if_missing 'azurerm_static_web_app.swiftcare[0]' "$STATIC_WEB_APP_ID"
fi

if [[ "$IMPORT_STATIC_WEB_APP" == "true" && "$IMPORT_CUSTOM_DOMAINS" == "true" ]]; then
  import_if_missing 'azurerm_static_web_app_custom_domain.apex[0]' \
    "${STATIC_WEB_APP_ID}/customDomains/${FRONTEND_DOMAIN}"
  if [[ -n "$FRONTEND_WWW_DOMAIN" ]]; then
    import_if_missing 'azurerm_static_web_app_custom_domain.www[0]' \
      "${STATIC_WEB_APP_ID}/customDomains/${FRONTEND_WWW_DOMAIN}"
  fi
fi

import_if_missing azurerm_user_assigned_identity.github_cd "$IDENTITY_ID"
import_if_missing 'azurerm_federated_identity_credential.github_cd["standard"]' \
  "${IDENTITY_ID}/federatedIdentityCredentials/github-azure-development"
import_if_missing 'azurerm_federated_identity_credential.github_cd["customized"]' \
  "${IDENTITY_ID}/federatedIdentityCredentials/github-any-branch-azure-development"

PRINCIPAL_ID=$(az identity show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$DEPLOYMENT_IDENTITY_NAME" \
  --query principalId \
  --output tsv)

ROLE_ASSIGNMENT_ID=$(az role assignment list \
  --assignee-object-id "$PRINCIPAL_ID" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}" \
  --role Contributor \
  --query "[0].id" \
  --output tsv)

if [[ -z "$ROLE_ASSIGNMENT_ID" ]]; then
  # A new environment has no assignment until the subscription Owner creates it; keep
  # deployment_role_assignment_enabled = false until then.
  echo "The GitHub identity has no Contributor assignment on ${RESOURCE_GROUP}; skipped." >&2
else
  import_if_missing 'azurerm_role_assignment.github_cd_contributor[0]' "$ROLE_ASSIGNMENT_ID"
fi

echo
echo "Import completed. Run terraform plan and inspect every proposed change."
