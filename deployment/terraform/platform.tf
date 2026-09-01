resource "azurerm_log_analytics_workspace" "swiftcare" {
  name                         = local.log_analytics_workspace_name
  location                     = azurerm_resource_group.swiftcare.location
  resource_group_name          = azurerm_resource_group.swiftcare.name
  sku                          = "PerGB2018"
  retention_in_days            = 30
  local_authentication_enabled = true
  tags                         = local.common_tags

  lifecycle {
    # Azure omits its legacy default from imported workspace responses.
    ignore_changes = [local_authentication_enabled]
  }
}

resource "azurerm_container_app_environment" "swiftcare" {
  name                       = local.container_apps_environment_name
  location                   = azurerm_resource_group.swiftcare.location
  resource_group_name        = azurerm_resource_group.swiftcare.name
  infrastructure_subnet_id   = azurerm_subnet.container_apps.id
  logs_destination           = "log-analytics"
  log_analytics_workspace_id = azurerm_log_analytics_workspace.swiftcare.id
  tags                       = local.common_tags

  lifecycle {
    # Azure materializes the built-in Consumption profile automatically.
    ignore_changes = [workload_profile]
  }
}
