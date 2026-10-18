# Window guard identity. The scheduled window-guard workflow signs in as this identity to stop
# running apps, the Kafka broker and MySQL once a demo window has closed or expired. It can only
# read and stop resources in this resource group and read the subscription's cost; it cannot
# start, create, change or delete anything. Its GitHub Environment holds no secrets and needs no
# reviewer, so a scheduled run is never left waiting for an approval.

resource "azurerm_user_assigned_identity" "window_guard" {
  count = var.window_guard_enabled ? 1 : 0

  name                = "${local.name_prefix}-window-guard"
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  tags = merge(local.common_tags, {
    purpose = "github-oidc-window-guard"
  })
}

resource "azurerm_federated_identity_credential" "window_guard" {
  for_each = var.window_guard_enabled ? var.window_guard_oidc_credentials : {}

  name                      = each.value.name
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  user_assigned_identity_id = azurerm_user_assigned_identity.window_guard[0].id
  subject                   = each.value.subject
}

# Role definitions and assignments need the subscription Owner, like the deployment identity's.
resource "azurerm_role_definition" "window_guard" {
  count = var.window_guard_enabled && var.deployment_role_assignment_enabled ? 1 : 0

  name        = "${local.name_prefix} window guard"
  scope       = azurerm_resource_group.swiftcare.id
  description = "Read and stop Container Apps, container groups and MySQL flexible servers in one resource group."

  permissions {
    actions = [
      "Microsoft.Resources/subscriptions/resourceGroups/read",
      "Microsoft.App/containerApps/read",
      "Microsoft.App/containerApps/stop/action",
      "Microsoft.ContainerInstance/containerGroups/read",
      "Microsoft.ContainerInstance/containerGroups/stop/action",
      "Microsoft.DBforMySQL/flexibleServers/read",
      "Microsoft.DBforMySQL/flexibleServers/stop/action",
    ]
  }

  assignable_scopes = [azurerm_resource_group.swiftcare.id]
}

resource "azurerm_role_assignment" "window_guard" {
  count = var.window_guard_enabled && var.deployment_role_assignment_enabled ? 1 : 0

  scope              = azurerm_resource_group.swiftcare.id
  role_definition_id = azurerm_role_definition.window_guard[0].role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.window_guard[0].principal_id
  principal_type     = "ServicePrincipal"
}

# Lets the guard report spend against the credit in its run summary; read-only.
resource "azurerm_role_assignment" "window_guard_cost_reader" {
  count = var.window_guard_enabled && var.deployment_role_assignment_enabled ? 1 : 0

  scope                = data.azurerm_subscription.current.id
  role_definition_name = "Cost Management Reader"
  principal_id         = azurerm_user_assigned_identity.window_guard[0].principal_id
  principal_type       = "ServicePrincipal"
}
