resource "azurerm_public_ip" "messaging" {
  count = var.messaging_enabled ? 1 : 0

  name                = local.messaging_public_ip_name
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  allocation_method   = "Static"
  sku                 = "Standard"
  tags                = local.common_tags
}

resource "azurerm_nat_gateway" "messaging" {
  count = var.messaging_enabled ? 1 : 0

  name                    = local.messaging_nat_gateway_name
  location                = azurerm_resource_group.swiftcare.location
  resource_group_name     = azurerm_resource_group.swiftcare.name
  sku_name                = "Standard"
  idle_timeout_in_minutes = 4
  tags                    = local.common_tags
}

resource "azurerm_nat_gateway_public_ip_association" "messaging" {
  count = var.messaging_enabled ? 1 : 0

  nat_gateway_id       = azurerm_nat_gateway.messaging[0].id
  public_ip_address_id = azurerm_public_ip.messaging[0].id
}

resource "azurerm_subnet_nat_gateway_association" "messaging" {
  count = var.messaging_enabled ? 1 : 0

  subnet_id      = azurerm_subnet.messaging.id
  nat_gateway_id = azurerm_nat_gateway.messaging[0].id
}

resource "azurerm_private_dns_zone" "messaging" {
  name                = local.messaging_private_dns_zone_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  tags                = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "messaging" {
  name                 = "messaging-vnet-link"
  private_dns_zone_id  = azurerm_private_dns_zone.messaging.id
  virtual_network_id   = azurerm_virtual_network.swiftcare.id
  registration_enabled = false
}

resource "azurerm_container_group" "messaging" {
  count = var.messaging_enabled ? 1 : 0

  name                = local.messaging_container_group_name
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  ip_address_type     = "Private"
  os_type             = "Linux"
  restart_policy      = "Always"
  subnet_ids          = [azurerm_subnet.messaging.id]
  tags                = local.common_tags

  container {
    name   = "zookeeper"
    image  = var.zookeeper_image
    cpu    = 0.5
    memory = 1.0

    ports {
      port     = 2181
      protocol = "TCP"
    }

    environment_variables = {
      ZOOKEEPER_CLIENT_PORT = "2181"
      ZOOKEEPER_TICK_TIME   = "2000"
    }
  }

  container {
    name   = "kafka"
    image  = var.kafka_image
    cpu    = 1.0
    memory = 2.0

    ports {
      port     = 9092
      protocol = "TCP"
    }

    environment_variables = {
      KAFKA_BROKER_ID                                = "1"
      KAFKA_ZOOKEEPER_CONNECT                        = "localhost:2181"
      KAFKA_LISTENERS                                = "PLAINTEXT://0.0.0.0:9092"
      KAFKA_ADVERTISED_LISTENERS                     = "PLAINTEXT://${local.kafka_bootstrap_servers}"
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP           = "PLAINTEXT:PLAINTEXT"
      KAFKA_INTER_BROKER_LISTENER_NAME               = "PLAINTEXT"
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR         = "1"
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR            = "1"
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR = "1"
    }
  }

  depends_on = [
    azurerm_nat_gateway_public_ip_association.messaging,
    azurerm_subnet_nat_gateway_association.messaging,
  ]
}

resource "azurerm_private_dns_a_record" "kafka" {
  count = var.messaging_enabled ? 1 : 0

  name                = "kafka"
  private_dns_zone_id = azurerm_private_dns_zone.messaging.id
  ttl                 = 60
  records             = [azurerm_container_group.messaging[0].ip_address]
  tags                = local.common_tags
}
