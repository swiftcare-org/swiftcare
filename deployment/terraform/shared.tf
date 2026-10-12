# Joining a shared Container Apps environment (production uses development's, because the
# subscription allows only one environment in total). This environment keeps its own network,
# MySQL server, messaging, DNS zones, identity, secrets and site; only the app hosting is shared.
# The shared environment runs in the other network, so the two networks are peered and this
# environment's private DNS zones are linked to it. The peering on the shared side and the DNS
# links are owned by this configuration's state.

data "azurerm_container_app_environment" "shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                = var.shared_container_apps_environment.name
  resource_group_name = var.shared_container_apps_environment.resource_group_name
}

data "azurerm_virtual_network" "shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                = var.shared_container_apps_environment.virtual_network_name
  resource_group_name = var.shared_container_apps_environment.resource_group_name
}

resource "azurerm_virtual_network_peering" "to_shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                         = "${local.name_prefix}-to-shared"
  resource_group_name          = azurerm_resource_group.swiftcare.name
  virtual_network_name         = azurerm_virtual_network.swiftcare.name
  remote_virtual_network_id    = data.azurerm_virtual_network.shared[0].id
  allow_virtual_network_access = true
  allow_forwarded_traffic      = false
}

resource "azurerm_virtual_network_peering" "from_shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                         = "shared-to-${local.name_prefix}"
  resource_group_name          = var.shared_container_apps_environment.resource_group_name
  virtual_network_name         = var.shared_container_apps_environment.virtual_network_name
  remote_virtual_network_id    = azurerm_virtual_network.swiftcare.id
  allow_virtual_network_access = true
  allow_forwarded_traffic      = false
}

# Apps in the shared environment resolve this environment's MySQL and Kafka names.
resource "azurerm_private_dns_zone_virtual_network_link" "mysql_shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                 = "mysql-shared-vnet-link"
  private_dns_zone_id  = azurerm_private_dns_zone.mysql.id
  virtual_network_id   = data.azurerm_virtual_network.shared[0].id
  registration_enabled = false
}

resource "azurerm_private_dns_zone_virtual_network_link" "messaging_shared" {
  count = local.create_container_apps_environment ? 0 : 1

  name                 = "messaging-shared-vnet-link"
  private_dns_zone_id  = azurerm_private_dns_zone.messaging.id
  virtual_network_id   = data.azurerm_virtual_network.shared[0].id
  registration_enabled = false
}

# Least privilege on the shared environment: the deployment identity may read it and place apps
# and jobs in it, but cannot change it or reach the other environment's apps. Creating a role
# definition or assignment needs the subscription Owner, so it follows the same switch as the
# resource-group assignment.
resource "azurerm_role_definition" "environment_join" {
  count = !local.create_container_apps_environment && var.deployment_role_assignment_enabled ? 1 : 0

  name        = "${local.name_prefix} Container Apps environment join"
  scope       = data.azurerm_container_app_environment.shared[0].id
  description = "Read the shared Container Apps environment and create apps and jobs in it."

  permissions {
    actions = [
      "Microsoft.App/managedEnvironments/read",
      "Microsoft.App/managedEnvironments/join/action",
    ]
  }

  assignable_scopes = [data.azurerm_container_app_environment.shared[0].id]
}

resource "azurerm_role_assignment" "github_cd_environment_join" {
  count = !local.create_container_apps_environment && var.deployment_role_assignment_enabled ? 1 : 0

  scope              = data.azurerm_container_app_environment.shared[0].id
  role_definition_id = azurerm_role_definition.environment_join[0].role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.github_cd.principal_id
  principal_type     = "ServicePrincipal"
}
