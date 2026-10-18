# The frontend is a Static Web App where the subscription allows one; otherwise CD serves it from a
# Container App (frontend/Dockerfile) and this file creates nothing.
resource "azurerm_static_web_app" "swiftcare" {
  count = var.static_web_app_enabled ? 1 : 0

  name                = var.static_web_app_name
  resource_group_name = azurerm_resource_group.swiftcare.name
  location            = coalesce(var.static_web_app_location, azurerm_resource_group.swiftcare.location)
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
  count = var.static_web_app_enabled && var.frontend_custom_domains_enabled ? 1 : 0

  static_web_app_id = azurerm_static_web_app.swiftcare[0].id
  domain_name       = var.frontend_domain
  validation_type   = var.frontend_domain_validation_type

  lifecycle {
    ignore_changes = [validation_type]
  }
}

resource "azurerm_static_web_app_custom_domain" "www" {
  count = var.static_web_app_enabled && var.frontend_custom_domains_enabled && var.frontend_www_domain != "" ? 1 : 0

  static_web_app_id = azurerm_static_web_app.swiftcare[0].id
  domain_name       = var.frontend_www_domain
  validation_type   = "cname-delegation"

  lifecycle {
    ignore_changes = [validation_type]
  }
}

# The site became conditional when production moved to a subscription without Static Web Apps;
# keep the existing development site.
moved {
  from = azurerm_static_web_app.swiftcare
  to   = azurerm_static_web_app.swiftcare[0]
}
