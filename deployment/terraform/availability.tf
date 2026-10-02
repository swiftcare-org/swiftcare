locals {
  availability_targets = {
    frontend = var.availability_frontend_url
    gateway  = var.availability_gateway_url
  }
}

resource "azurerm_application_insights_standard_web_test" "availability" {
  for_each                = local.availability_targets
  name                    = "${var.project_name}-${each.key}-availability"
  resource_group_name     = azurerm_resource_group.swiftcare.name
  location                = azurerm_application_insights.swiftcare.location
  application_insights_id = azurerm_application_insights.swiftcare.id
  geo_locations           = var.availability_locations
  enabled                 = var.availability_enabled
  frequency               = 300
  timeout                 = 30
  retry_enabled           = true
  description             = "Public ${each.key} HTTPS availability; gateway health proves liveness only."
  tags = merge(local.common_tags, {
    "hidden-link:${azurerm_application_insights.swiftcare.id}" = "Resource"
  })

  request {
    url                              = each.value
    http_verb                        = "GET"
    follow_redirects_enabled         = false
    parse_dependent_requests_enabled = false
  }

  validation_rules {
    expected_status_code = 200
    ssl_check_enabled    = true
  }
}

resource "azurerm_monitor_action_group" "availability" {
  name                = "${var.project_name}-availability-team"
  resource_group_name = azurerm_resource_group.swiftcare.name
  short_name          = "swc-uptime"
  tags                = local.common_tags

  dynamic "email_receiver" {
    for_each = var.availability_alert_emails
    content {
      name                    = "team-${email_receiver.key + 1}"
      email_address           = email_receiver.value
      use_common_alert_schema = true
    }
  }

  lifecycle {
    precondition {
      condition     = !var.availability_enabled || length(var.availability_alert_emails) > 0
      error_message = "Enabled availability monitoring requires team recipients in ignored terraform.tfvars."
    }
  }
}

resource "azurerm_monitor_metric_alert" "availability" {
  for_each                 = local.availability_targets
  name                     = "${var.project_name}-${each.key}-availability-failure"
  resource_group_name      = azurerm_resource_group.swiftcare.name
  scopes                   = [azurerm_application_insights_standard_web_test.availability[each.key].id, azurerm_application_insights.swiftcare.id]
  target_resource_type     = "Microsoft.Insights/components"
  target_resource_location = azurerm_application_insights.swiftcare.location
  description              = "${each.key} failed from at least ${var.availability_failed_location_count} locations. Check Cloudflare and origin liveness."
  enabled                  = var.availability_enabled
  severity                 = 1
  frequency                = "PT1M"
  window_size              = "PT5M"
  auto_mitigate            = true
  tags                     = local.common_tags

  application_insights_web_test_location_availability_criteria {
    web_test_id           = azurerm_application_insights_standard_web_test.availability[each.key].id
    component_id          = azurerm_application_insights.swiftcare.id
    failed_location_count = var.availability_failed_location_count
  }

  action {
    action_group_id = azurerm_monitor_action_group.availability.id
  }
}
