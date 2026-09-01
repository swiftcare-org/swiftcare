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
  value       = azurerm_container_app_environment.swiftcare.name
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
  description = "GitHub Environment value for AZURE_STATIC_WEB_APP_NAME."
  value       = azurerm_static_web_app.swiftcare.name
}

output "static_web_app_default_hostname" {
  description = "Azure-generated frontend hostname retained as a fallback."
  value       = azurerm_static_web_app.swiftcare.default_host_name
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

output "messaging_container_group_name" {
  description = "GitHub Environment value for KAFKA_CONTAINER_GROUP."
  value       = local.messaging_container_group_name
}

output "kafka_container_name" {
  description = "GitHub Environment value for KAFKA_CONTAINER_NAME."
  value       = "kafka"
}

output "zookeeper_container_name" {
  description = "GitHub Environment value for ZOOKEEPER_CONTAINER_NAME."
  value       = "zookeeper"
}

output "messaging_private_ip" {
  description = "Current private IP of the ephemeral messaging container group."
  value       = var.messaging_enabled ? azurerm_container_group.messaging[0].ip_address : null
}
