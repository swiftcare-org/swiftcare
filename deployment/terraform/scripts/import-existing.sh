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
MYSQL_SERVER_NAME="${MYSQL_SERVER_NAME:-${PROJECT_NAME}-mysql}"
STATIC_WEB_APP_NAME="${STATIC_WEB_APP_NAME:-${PROJECT_NAME}-web}"
DEPLOYMENT_IDENTITY_NAME="${DEPLOYMENT_IDENTITY_NAME:-${PROJECT_NAME}-github-cd}"
FRONTEND_DOMAIN="${FRONTEND_DOMAIN:-swiftcare.me}"
FRONTEND_WWW_DOMAIN="${FRONTEND_WWW_DOMAIN:-www.swiftcare.me}"
IMPORT_CUSTOM_DOMAINS="${IMPORT_CUSTOM_DOMAINS:-false}"
IMPORT_MESSAGING="${IMPORT_MESSAGING:-true}"

BASE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers"
VNET_ID="${BASE}/Microsoft.Network/virtualNetworks/${PROJECT_NAME}-vnet"
MYSQL_SERVER_ID="${BASE}/Microsoft.DBforMySQL/flexibleServers/${MYSQL_SERVER_NAME}"
MYSQL_DNS_ID="${BASE}/Microsoft.Network/privateDnsZones/${PROJECT_NAME}.private.mysql.database.azure.com"
MESSAGING_DNS_ID="${BASE}/Microsoft.Network/privateDnsZones/${PROJECT_NAME}.internal"
NAT_ID="${BASE}/Microsoft.Network/natGateways/${PROJECT_NAME}-messaging-nat"
PUBLIC_IP_ID="${BASE}/Microsoft.Network/publicIPAddresses/${PROJECT_NAME}-messaging-nat-ip"
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
import_if_missing azurerm_subnet.container_apps "${VNET_ID}/subnets/containerapps-subnet"
import_if_missing azurerm_subnet.mysql "${VNET_ID}/subnets/mysql-subnet"
import_if_missing azurerm_subnet.messaging "${VNET_ID}/subnets/messaging-subnet"

import_if_missing azurerm_log_analytics_workspace.swiftcare \
  "${BASE}/Microsoft.OperationalInsights/workspaces/${PROJECT_NAME}-logs"
import_if_missing azurerm_container_app_environment.swiftcare \
  "${BASE}/Microsoft.App/managedEnvironments/${PROJECT_NAME}-aca-env"

import_if_missing azurerm_private_dns_zone.mysql "$MYSQL_DNS_ID"
import_if_missing azurerm_private_dns_zone_virtual_network_link.mysql \
  "${MYSQL_DNS_ID}/virtualNetworkLinks/mysql-vnet-link"
import_if_missing azurerm_mysql_flexible_server.swiftcare "$MYSQL_SERVER_ID"
import_if_missing azurerm_mysql_flexible_database.auth \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_auth"
import_if_missing azurerm_mysql_flexible_database.patient \
  "${MYSQL_SERVER_ID}/databases/${PROJECT_NAME}_patient"

import_if_missing azurerm_private_dns_zone.messaging "$MESSAGING_DNS_ID"
import_if_missing azurerm_private_dns_zone_virtual_network_link.messaging \
  "${MESSAGING_DNS_ID}/virtualNetworkLinks/messaging-vnet-link"

if [[ "$IMPORT_MESSAGING" == "true" ]]; then
  import_if_missing 'azurerm_public_ip.messaging[0]' "$PUBLIC_IP_ID"
  import_if_missing 'azurerm_nat_gateway.messaging[0]' "$NAT_ID"
  import_if_missing 'azurerm_nat_gateway_public_ip_association.messaging[0]' \
    "${NAT_ID}|${PUBLIC_IP_ID}"
  import_if_missing 'azurerm_subnet_nat_gateway_association.messaging[0]' \
    "${VNET_ID}/subnets/messaging-subnet"
  import_if_missing 'azurerm_container_group.messaging[0]' \
    "${BASE}/Microsoft.ContainerInstance/containerGroups/${PROJECT_NAME}-messaging"
  import_if_missing 'azurerm_private_dns_a_record.kafka[0]' \
    "${MESSAGING_DNS_ID}/A/kafka"
fi

import_if_missing azurerm_static_web_app.swiftcare "$STATIC_WEB_APP_ID"

if [[ "$IMPORT_CUSTOM_DOMAINS" == "true" ]]; then
  import_if_missing 'azurerm_static_web_app_custom_domain.apex[0]' \
    "${STATIC_WEB_APP_ID}/customDomains/${FRONTEND_DOMAIN}"
  import_if_missing 'azurerm_static_web_app_custom_domain.www[0]' \
    "${STATIC_WEB_APP_ID}/customDomains/${FRONTEND_WWW_DOMAIN}"
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
  echo "The GitHub identity has no Contributor assignment on ${RESOURCE_GROUP}." >&2
  exit 1
fi

import_if_missing azurerm_role_assignment.github_cd_contributor "$ROLE_ASSIGNMENT_ID"

echo
echo "Import completed. Run terraform plan and inspect every proposed change."
