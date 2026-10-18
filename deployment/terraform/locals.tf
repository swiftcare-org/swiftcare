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
  kafka_app_name                  = "${local.name_prefix}-kafka"
  github_deployment_identity_name = "${local.name_prefix}-github-cd"
  auth_database_name              = "${var.project_name}_auth"
  patient_database_name           = "${var.project_name}_patient"
  queue_database_name             = "${var.project_name}_queue"
  medical_record_database_name    = "${var.project_name}_medical_record"
  prescription_database_name      = "${var.project_name}_prescription"
  notification_database_name      = "${var.project_name}_notification"

  messaging_container_app   = var.messaging_enabled && var.messaging_host == "container_app"
  messaging_container_group = var.messaging_enabled && var.messaging_host == "container_group"

  # Apps in a Container Apps environment reach an internal TCP app by its name and exposed port.
  kafka_bootstrap_servers = var.messaging_host == "container_app" ? "${local.kafka_app_name}:9092" : "kafka.${local.messaging_private_dns_zone_name}:9092"

  container_apps_environment_id = local.create_container_apps_environment ? azurerm_container_app_environment.swiftcare[0].id : data.azurerm_container_app_environment.shared[0].id

  # Single-node KRaft settings for the Kafka Container App (apache/kafka image conventions).
  kafka_app_settings = {
    KAFKA_NODE_ID                                  = "1"
    KAFKA_PROCESS_ROLES                            = "broker,controller"
    KAFKA_LISTENERS                                = "PLAINTEXT://:9092,CONTROLLER://localhost:9093"
    KAFKA_ADVERTISED_LISTENERS                     = "PLAINTEXT://${local.kafka_bootstrap_servers}"
    KAFKA_CONTROLLER_LISTENER_NAMES                = "CONTROLLER"
    KAFKA_LISTENER_SECURITY_PROTOCOL_MAP           = "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT"
    KAFKA_CONTROLLER_QUORUM_VOTERS                 = "1@localhost:9093"
    KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR         = "1"
    KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR = "1"
    KAFKA_TRANSACTION_STATE_LOG_MIN_ISR            = "1"
    KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS         = "0"
    KAFKA_NUM_PARTITIONS                           = "1"
    # Broker data is ephemeral, as with the container group: a restart starts with empty topics.
    KAFKA_LOG_DIRS = "/tmp/kraft-combined-logs"
    # Measured use was about 600 MB for Kafka and ZooKeeper together; keep the heap well inside
    # the container memory.
    KAFKA_HEAP_OPTS = "-Xms384m -Xmx384m"
  }
}
