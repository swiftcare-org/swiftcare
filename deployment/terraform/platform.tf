resource "azurerm_log_analytics_workspace" "swiftcare" {
  name                = local.log_analytics_workspace_name
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.common_tags
}

resource "azurerm_container_app_environment" "swiftcare" {
  name                       = local.container_apps_environment_name
  location                   = azurerm_resource_group.swiftcare.location
  resource_group_name        = azurerm_resource_group.swiftcare.name
  infrastructure_subnet_id   = azurerm_subnet.container_apps.id
  logs_destination           = "log-analytics"
  log_analytics_workspace_id = azurerm_log_analytics_workspace.swiftcare.id
  tags                       = local.common_tags
}
