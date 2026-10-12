locals {
  common_tags = merge(
    {
      project     = var.project_name
      environment = var.environment
    },
    var.additional_tags
  )

  # Azure resource names use name_prefix, so a second environment can sit beside the first.
  # Database names keep project_name: the CD release manifest expects the same names everywhere.
  name_prefix = coalesce(var.name_prefix, var.project_name)

  # The subscription allows one Container Apps environment in total, so only one configuration
  # creates it; the other joins it (see shared.tf).
  create_container_apps_environment = var.shared_container_apps_environment == null

  virtual_network_name            = "${local.name_prefix}-vnet"
  log_analytics_workspace_name    = "${local.name_prefix}-logs"
  application_insights_name       = "${local.name_prefix}-appinsights"
  container_apps_environment_name = "${local.name_prefix}-aca-env"
  mysql_private_dns_zone_name     = "${local.name_prefix}.private.mysql.database.azure.com"
  messaging_private_dns_zone_name = "${local.name_prefix}.internal"
  messaging_container_group_name  = "${local.name_prefix}-messaging"
  messaging_nat_gateway_name      = "${local.name_prefix}-messaging-nat"
  messaging_public_ip_name        = "${local.name_prefix}-messaging-nat-ip"
  github_deployment_identity_name = "${local.name_prefix}-github-cd"
  auth_database_name              = "${var.project_name}_auth"
  patient_database_name           = "${var.project_name}_patient"
  queue_database_name             = "${var.project_name}_queue"
  medical_record_database_name    = "${var.project_name}_medical_record"
  prescription_database_name      = "${var.project_name}_prescription"
  notification_database_name      = "${var.project_name}_notification"
  kafka_bootstrap_servers         = "kafka.${local.messaging_private_dns_zone_name}:9092"
}
