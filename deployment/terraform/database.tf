resource "azurerm_private_dns_zone" "mysql" {
  name                = local.mysql_private_dns_zone_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  tags                = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "mysql" {
  name                 = "mysql-vnet-link"
  private_dns_zone_id  = azurerm_private_dns_zone.mysql.id
  virtual_network_id   = azurerm_virtual_network.swiftcare.id
  registration_enabled = false
}

resource "azurerm_mysql_flexible_server" "swiftcare" {
  name                = var.mysql_server_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  location            = azurerm_resource_group.swiftcare.location

  administrator_login               = var.mysql_administrator_login
  administrator_password_wo         = var.mysql_administrator_password
  administrator_password_wo_version = var.mysql_administrator_password_version

  delegated_subnet_id = azurerm_subnet.mysql.id
  private_dns_zone_id = azurerm_private_dns_zone.mysql.id

  backup_retention_days        = var.mysql_backup_retention_days
  geo_redundant_backup_enabled = false
  sku_name                     = var.mysql_sku_name
  version                      = var.mysql_version

  storage {
    auto_grow_enabled = true
    size_gb           = var.mysql_storage_size_gb
  }

  tags = local.common_tags

  depends_on = [azurerm_private_dns_zone_virtual_network_link.mysql]

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mysql_flexible_database" "auth" {
  name                = local.auth_database_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  server_name         = azurerm_mysql_flexible_server.swiftcare.name
  charset             = "utf8mb4"
  collation           = "utf8mb4_0900_ai_ci"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mysql_flexible_database" "patient" {
  name                = local.patient_database_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  server_name         = azurerm_mysql_flexible_server.swiftcare.name
  charset             = "utf8mb4"
  collation           = "utf8mb4_0900_ai_ci"

  lifecycle {
    prevent_destroy = true
  }
}
