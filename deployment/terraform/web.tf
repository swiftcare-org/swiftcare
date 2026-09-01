resource "azurerm_static_web_app" "swiftcare" {
  name                = var.static_web_app_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  location            = azurerm_resource_group.swiftcare.location
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = local.common_tags

  lifecycle {
    ignore_changes = [
      repository_branch,
      repository_url,
    ]
  }
}

resource "azurerm_static_web_app_custom_domain" "apex" {
  count = var.frontend_custom_domains_enabled ? 1 : 0

  static_web_app_id = azurerm_static_web_app.swiftcare.id
  domain_name       = var.frontend_domain
  validation_type   = "dns-txt-token"

  lifecycle {
    ignore_changes = [validation_type]
  }
}

resource "azurerm_static_web_app_custom_domain" "www" {
  count = var.frontend_custom_domains_enabled ? 1 : 0

  static_web_app_id = azurerm_static_web_app.swiftcare.id
  domain_name       = var.frontend_www_domain
  validation_type   = "cname-delegation"

  lifecycle {
    ignore_changes = [validation_type]
  }
}
