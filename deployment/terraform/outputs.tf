output "resource_group_name" {
  description = "GitHub Environment value for AZURE_RESOURCE_GROUP."
  value       = azurerm_resource_group.swiftcare.name
}

output "azure_subscription_id" {
  description = "GitHub Environment value for AZURE_SUBSCRIPTION_ID."
  value       = data.azurerm_client_config.current.subscription_id
}

output "azure_tenant_id" {
  description = "GitHub Environment value for AZURE_TENANT_ID."
  value       = data.azurerm_client_config.current.tenant_id
}

output "azure_location" {
  description = "GitHub Environment value for AZURE_LOCATION."
  value       = azurerm_resource_group.swiftcare.location
}

output "container_apps_environment_name" {
  description = "GitHub Environment value for AZURE_CONTAINERAPPS_ENVIRONMENT."
  value       = local.create_container_apps_environment ? azurerm_container_app_environment.swiftcare[0].name : data.azurerm_container_app_environment.shared[0].name
}

output "container_apps_environment_resource_group" {
  description = "GitHub Environment value for AZURE_CONTAINERAPPS_ENVIRONMENT_RESOURCE_GROUP (the shared environment's group; differs from AZURE_RESOURCE_GROUP in production)."
  value       = local.create_container_apps_environment ? azurerm_resource_group.swiftcare.name : var.shared_container_apps_environment.resource_group_name
}

output "application_insights_name" {
  description = "Application Insights resource that receives telemetry from the services."
  value       = azurerm_application_insights.swiftcare.name
}

output "application_insights_connection_string" {
  description = "GitHub Environment secret for APPLICATIONINSIGHTS_CONNECTION_STRING."
  value       = azurerm_application_insights.swiftcare.connection_string
  sensitive   = true
}

output "mysql_server_name" {
  description = "GitHub Environment value for MYSQL_SERVER_NAME."
  value       = azurerm_mysql_flexible_server.swiftcare.name
}

output "mysql_fqdn" {
  description = "GitHub Environment value for MYSQL_FQDN."
  value       = azurerm_mysql_flexible_server.swiftcare.fqdn
}

output "static_web_app_name" {
  description = "GitHub Environment value for AZURE_STATIC_WEB_APP_NAME (static_web_app_enabled only)."
  value       = var.static_web_app_enabled ? azurerm_static_web_app.swiftcare[0].name : null
}

output "static_web_app_default_hostname" {
  description = "Azure-generated frontend hostname retained as a fallback."
  value       = var.static_web_app_enabled ? azurerm_static_web_app.swiftcare[0].default_host_name : null
}

output "github_cd_client_id" {
  description = "GitHub Environment value for AZURE_CLIENT_ID."
  value       = azurerm_user_assigned_identity.github_cd.client_id
}

output "github_cd_principal_id" {
  description = "Object ID used when auditing the deployment role assignment."
  value       = azurerm_user_assigned_identity.github_cd.principal_id
}

output "kafka_bootstrap_servers" {
  description = "GitHub Environment value for KAFKA_BOOTSTRAP_SERVERS when messaging is enabled."
  value       = var.messaging_enabled ? local.kafka_bootstrap_servers : null
}

output "kafka_app_name" {
  description = "GitHub Environment value for KAFKA_APP (messaging_host = container_app)."
  value       = var.messaging_host == "container_app" ? local.kafka_app_name : null
}

output "messaging_container_group_name" {
  description = "GitHub Environment value for KAFKA_CONTAINER_GROUP (messaging_host = container_group only)."
  value       = var.messaging_host == "container_group" ? local.messaging_container_group_name : null
}

output "kafka_container_name" {
  description = "GitHub Environment value for KAFKA_CONTAINER_NAME (messaging_host = container_group only)."
  value       = var.messaging_host == "container_group" ? "kafka" : null
}

output "zookeeper_container_name" {
  description = "GitHub Environment value for ZOOKEEPER_CONTAINER_NAME (messaging_host = container_group only)."
  value       = var.messaging_host == "container_group" ? "zookeeper" : null
}

output "messaging_private_ip" {
  description = "Current private IP of the ephemeral messaging container group (messaging_host = container_group only)."
  value       = local.messaging_container_group ? azurerm_container_group.messaging[0].ip_address : null
}

output "window_guard_client_id" {
  description = "AZURE_CLIENT_ID of the window guard's GitHub Environment (window_guard_enabled only)."
  value       = var.window_guard_enabled ? azurerm_user_assigned_identity.window_guard[0].client_id : null
}
