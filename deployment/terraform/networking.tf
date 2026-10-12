resource "azurerm_virtual_network" "swiftcare" {
  name                = local.virtual_network_name
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  address_space       = var.virtual_network_address_space
  tags                = local.common_tags
}

# Only the configuration that creates the Container Apps environment needs this subnet.
resource "azurerm_subnet" "container_apps" {
  count = local.create_container_apps_environment ? 1 : 0

  name                 = "containerapps-subnet"
  resource_group_name  = azurerm_resource_group.swiftcare.name
  virtual_network_name = azurerm_virtual_network.swiftcare.name
  address_prefixes     = var.container_apps_subnet_prefixes

  delegation {
    # Retain the delegation name assigned when the existing subnet was created.
    name = "0"

    service_delegation {
      name = "Microsoft.App/environments"
      actions = [
        "Microsoft.Network/virtualNetworks/subnets/join/action",
      ]
    }
  }
}

resource "azurerm_subnet" "mysql" {
  name                 = "mysql-subnet"
  resource_group_name  = azurerm_resource_group.swiftcare.name
  virtual_network_name = azurerm_virtual_network.swiftcare.name
  address_prefixes     = var.mysql_subnet_prefixes

  delegation {
    # Retain the delegation name assigned when the existing subnet was created.
    name = "0"

    service_delegation {
      name = "Microsoft.DBforMySQL/flexibleServers"
      actions = [
        "Microsoft.Network/virtualNetworks/subnets/join/action",
      ]
    }
  }
}

resource "azurerm_subnet" "messaging" {
  name                 = "messaging-subnet"
  resource_group_name  = azurerm_resource_group.swiftcare.name
  virtual_network_name = azurerm_virtual_network.swiftcare.name
  address_prefixes     = var.messaging_subnet_prefixes

  delegation {
    # Retain the delegation name assigned when the existing subnet was created.
    name = "0"

    service_delegation {
      name = "Microsoft.ContainerInstance/containerGroups"
      actions = [
        "Microsoft.Network/virtualNetworks/subnets/action",
      ]
    }
  }
}

# The subnet became conditional in SWC-161; keep the existing development subnet.
moved {
  from = azurerm_subnet.container_apps
  to   = azurerm_subnet.container_apps[0]
}
