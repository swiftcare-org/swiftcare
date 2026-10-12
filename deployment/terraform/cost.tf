locals {
  # Actual spend at 50, 80 and 100 percent, and a forecast above the full budget.
  budget_notifications = {
    actual-50      = { threshold = 50, threshold_type = "Actual" }
    actual-80      = { threshold = 80, threshold_type = "Actual" }
    actual-100     = { threshold = 100, threshold_type = "Actual" }
    forecasted-100 = { threshold = 100, threshold_type = "Forecasted" }
  }
}

# Alerts only; a budget never stops or scales resources. Recipients are the team
# addresses from ignored terraform.tfvars plus the subscription Owner role.
resource "azurerm_consumption_budget_resource_group" "monthly" {
  count = var.budget_monthly_amount > 0 ? 1 : 0

  name              = "${local.name_prefix}-monthly-budget"
  resource_group_id = azurerm_resource_group.swiftcare.id
  amount            = var.budget_monthly_amount
  time_grain        = "Monthly"

  time_period {
    start_date = var.budget_start_date
  }

  dynamic "notification" {
    for_each = local.budget_notifications
    content {
      enabled        = true
      operator       = "GreaterThan"
      threshold      = notification.value.threshold
      threshold_type = notification.value.threshold_type
      contact_emails = var.availability_alert_emails
      contact_roles  = ["Owner"]
    }
  }
}
