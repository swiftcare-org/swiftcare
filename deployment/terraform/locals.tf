locals {
  common_tags = merge(
    {
      project     = var.project_name
      environment = var.environment
    },
    var.additional_tags
  )

  virtual_network_name            = "${var.project_name}-vnet"
  log_analytics_workspace_name    = "${var.project_name}-logs"
  container_apps_environment_name = "${var.project_name}-aca-env"
  mysql_private_dns_zone_name     = "${var.project_name}.private.mysql.database.azure.com"
  messaging_private_dns_zone_name = "${var.project_name}.internal"
  messaging_container_group_name  = "${var.project_name}-messaging"
  messaging_nat_gateway_name      = "${var.project_name}-messaging-nat"
  messaging_public_ip_name        = "${var.project_name}-messaging-nat-ip"
  github_deployment_identity_name = "${var.project_name}-github-cd"
  auth_database_name              = "${var.project_name}_auth"
  patient_database_name           = "${var.project_name}_patient"
  kafka_bootstrap_servers         = "kafka.${local.messaging_private_dns_zone_name}:9092"
}
