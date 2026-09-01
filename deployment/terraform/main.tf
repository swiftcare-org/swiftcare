resource "azurerm_resource_group" "swiftcare" {
  name     = var.resource_group_name
  location = var.location
  tags = merge(local.common_tags, {
    sprint = var.sprint_name
  })
}
