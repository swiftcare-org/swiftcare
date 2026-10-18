variable "subscription_id" {
  description = "Azure subscription that owns the SwiftCare development environment."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.subscription_id))
    error_message = "subscription_id must be an Azure subscription GUID."
  }
}

variable "location" {
  description = "Azure region available to the current student subscription."
  type        = string
  default     = "eastasia"
}

variable "environment" {
  description = "Environment name used in Azure tags and GitHub deployment configuration."
  type        = string
  default     = "azure-development"
}

variable "sprint_name" {
  description = "Sprint recorded on the application resource-group tag."
  type        = string
  default     = "sprint-1"
}

variable "project_name" {
  description = "Prefix used by SwiftCare Azure resources."
  type        = string
  default     = "swiftcare"

  validation {
    condition     = can(regex("^[a-z0-9-]+$", var.project_name))
    error_message = "project_name may contain only lowercase letters, numbers, and hyphens."
  }
}

variable "name_prefix" {
  description = "Prefix for Azure resource names in this environment, for example swiftcare-prod. Empty uses project_name, which keeps the development names."
  type        = string
  default     = ""

  validation {
    condition     = var.name_prefix == "" || can(regex("^[a-z0-9-]+$", var.name_prefix))
    error_message = "name_prefix may contain only lowercase letters, numbers, and hyphens."
  }
}

variable "resource_group_name" {
  description = "Resource group containing the application environment."
  type        = string
  default     = "swiftcare-rg"
}

variable "virtual_network_address_space" {
  description = "Address space for the shared SwiftCare virtual network."
  type        = list(string)
  default     = ["10.20.0.0/16"]
}

variable "shared_container_apps_environment" {
  description = "Run this environment's apps in an existing Container Apps environment instead of creating one. The subscription allows only one environment, so production sets development's; null creates the environment, as development does."
  type = object({
    name                 = string
    resource_group_name  = string
    virtual_network_name = string
  })
  default = null
}

variable "container_apps_subnet_prefixes" {
  description = "Delegated subnet used by the Container Apps environment."
  type        = list(string)
  default     = ["10.20.0.0/23"]
}

variable "mysql_subnet_prefixes" {
  description = "Delegated subnet used by MySQL Flexible Server."
  type        = list(string)
  default     = ["10.20.2.0/28"]
}

variable "messaging_subnet_prefixes" {
  description = "Delegated subnet used by the legacy Kafka and ZooKeeper container group (messaging_host = container_group)."
  type        = list(string)
  default     = ["10.20.3.0/27"]
}

variable "mysql_server_name" {
  description = "Globally unique MySQL Flexible Server name."
  type        = string
  default     = "swiftcare-mysql"
}

variable "mysql_administrator_login" {
  description = "Administrative login used to provision the MySQL server."
  type        = string
  default     = "swiftcareadmin"
}

variable "mysql_administrator_password" {
  description = "MySQL administrator password supplied through TF_VAR_mysql_administrator_password."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.mysql_administrator_password) >= 8 && length(var.mysql_administrator_password) <= 128
    error_message = "mysql_administrator_password must contain between 8 and 128 characters."
  }
}

variable "mysql_administrator_password_version" {
  description = "Write-only MySQL administrator password version. Increment from zero only when intentionally rotating the password."
  type        = number
  default     = 0
}

variable "mysql_sku_name" {
  description = "Low-cost MySQL Flexible Server SKU."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "mysql_version" {
  description = "MySQL version shared by local, CI and Azure environments."
  type        = string
  default     = "8.4"
}

variable "mysql_storage_size_gb" {
  description = "MySQL provisioned storage in GiB. Azure cannot reduce this value."
  type        = number
  default     = 32
}

variable "mysql_backup_retention_days" {
  description = "Number of days Azure retains MySQL backups."
  type        = number
  default     = 7
}

variable "static_web_app_name" {
  description = "Globally unique Azure Static Web App name."
  type        = string
  default     = "swiftcare-web"
}

variable "static_web_app_location" {
  description = "Region of the Static Web App. Empty uses location; set it when location does not offer Static Web Apps (production: eastasia while the rest is in koreacentral)."
  type        = string
  default     = ""
}

variable "frontend_custom_domains_enabled" {
  description = "Manage the existing frontend custom domains after DNS validation."
  type        = bool
  default     = false
}

variable "frontend_domain" {
  description = "Primary custom domain attached to the Static Web App, for example swiftcare.me or dev.swiftcare.me."
  type        = string
  default     = "swiftcare.me"
}

variable "frontend_domain_validation_type" {
  description = "Validation for frontend_domain: dns-txt-token for an apex domain, cname-delegation for a subdomain such as dev.swiftcare.me."
  type        = string
  default     = "dns-txt-token"

  validation {
    condition     = contains(["dns-txt-token", "cname-delegation"], var.frontend_domain_validation_type)
    error_message = "frontend_domain_validation_type must be dns-txt-token or cname-delegation."
  }
}

variable "frontend_www_domain" {
  description = "Optional second custom domain (CNAME delegation). Empty attaches none, as for development."
  type        = string
  default     = "www.swiftcare.me"
}

variable "messaging_enabled" {
  description = "Whether the Kafka broker exists (and, for the legacy container group host, its paid NAT gateway)."
  type        = bool
  default     = true
}

variable "messaging_host" {
  description = "Where Kafka runs: container_app (one KRaft broker in the Container Apps environment, no NAT gateway, stoppable) or container_group (legacy Kafka and ZooKeeper container group behind a NAT gateway)."
  type        = string
  default     = "container_app"
  validation {
    condition     = contains(["container_app", "container_group"], var.messaging_host)
    error_message = "messaging_host must be container_app or container_group."
  }
}

variable "kafka_kraft_image" {
  description = "Kafka image for the Container App broker; runs in KRaft mode without ZooKeeper."
  type        = string
  default     = "apache/kafka:3.9.0"
}

variable "kafka_app_cpu" {
  description = "vCPU for the Kafka Container App. Measured broker use was about 0.005 vCPU."
  type        = number
  default     = 0.5
}

variable "kafka_app_memory" {
  description = "Memory for the Kafka Container App; must form a valid Consumption pair with kafka_app_cpu (0.5 vCPU with 1Gi)."
  type        = string
  default     = "1Gi"
}

variable "zookeeper_image" {
  description = "ZooKeeper image used by the project architecture."
  type        = string
  default     = "confluentinc/cp-zookeeper:7.6.1"
}

variable "kafka_image" {
  description = "Kafka image used by the project architecture."
  type        = string
  default     = "confluentinc/cp-kafka:7.6.1"
}

variable "github_oidc_credentials" {
  description = "GitHub OIDC credential names and subjects accepted by the deployment identity."
  type = map(object({
    name    = string
    subject = string
  }))
  default = {
    standard = {
      name    = "github-azure-development"
      subject = "repo:swiftcare-org/swiftcare:environment:azure-development"
    }
    customized = {
      name    = "github-any-branch-azure-development"
      subject = "repo:swiftcare-org@317644346/swiftcare@1336209668:environment:azure-development"
    }
  }
}

variable "deployment_role_assignment_enabled" {
  description = "Manage the deployment identity's Contributor assignment. Creating it needs the subscription Owner; keep false until the Owner has created it, then import it."
  type        = bool
  default     = true
}

variable "additional_tags" {
  description = "Optional tags merged with the standard project and environment tags."
  type        = map(string)
  default     = {}
}

variable "budget_monthly_amount" {
  description = "Monthly cost budget for the application resource group in the billing currency (USD for Azure for Students). Set 0 to omit the budget."
  type        = number
  default     = 30
  validation {
    condition     = var.budget_monthly_amount >= 0
    error_message = "budget_monthly_amount must be zero (no budget) or a positive amount."
  }
}

variable "budget_start_date" {
  description = "First day of the first budget month, in UTC. Changing it replaces the budget."
  type        = string
  default     = "2026-10-01T00:00:00Z"
  validation {
    condition     = can(regex("^[0-9]{4}-[0-9]{2}-01T00:00:00Z$", var.budget_start_date))
    error_message = "budget_start_date must be the first day of a month, for example 2026-10-01T00:00:00Z."
  }
}

variable "subscription_budget_amount" {
  description = "Budget for the whole subscription in the billing currency, normally the remaining Azure for Students credit. Set 0 to omit it."
  type        = number
  default     = 0
  validation {
    condition     = var.subscription_budget_amount >= 0
    error_message = "subscription_budget_amount must be zero (no budget) or a positive amount."
  }
}

variable "subscription_budget_time_grain" {
  description = "Reset period of the subscription budget. Annually follows a one-off credit; Monthly suits a recurring allowance."
  type        = string
  default     = "Annually"
  validation {
    condition     = contains(["Monthly", "Quarterly", "Annually"], var.subscription_budget_time_grain)
    error_message = "subscription_budget_time_grain must be Monthly, Quarterly or Annually."
  }
}

variable "subscription_budget_start_date" {
  description = "First day of the first subscription budget period, in UTC. Changing it replaces the budget."
  type        = string
  default     = "2026-10-01T00:00:00Z"
  validation {
    condition     = can(regex("^[0-9]{4}-[0-9]{2}-01T00:00:00Z$", var.subscription_budget_start_date))
    error_message = "subscription_budget_start_date must be the first day of a month, for example 2026-10-01T00:00:00Z."
  }
}

variable "availability_frequency" {
  description = "Seconds between availability test runs. Each run in each location is billed, so 900 costs a third of 300; detection takes up to one interval."
  type        = number
  default     = 900
  validation {
    condition     = contains([300, 900], var.availability_frequency)
    error_message = "availability_frequency must be 300 or 900 so the alert window can match it."
  }
}

variable "availability_enabled" {
  description = "Enable both tests and alerts during the agreed uptime window; disabling retains resources."
  type        = bool
  default     = true
}

variable "availability_frontend_url" {
  description = "Public frontend target; override only for a reviewed environment or authorized failure drill."
  type        = string
  default     = "https://swiftcare.me"
  validation {
    condition     = can(regex("^https://[^/?#@[:space:]]+(/[^?#[:space:]]*)?$", var.availability_frontend_url))
    error_message = "Use an HTTPS URL without credentials, query strings or fragments."
  }
}

variable "availability_gateway_url" {
  description = "Public gateway liveness target; override only for a reviewed environment or authorized failure drill."
  type        = string
  default     = "https://api.swiftcare.me/health"
  validation {
    condition     = can(regex("^https://[^/?#@[:space:]]+(/[^?#[:space:]]*)?$", var.availability_gateway_url))
    error_message = "Use an HTTPS URL without credentials, query strings or fragments."
  }
}

variable "availability_locations" {
  description = "Azure availability population IDs, independent of the resource deployment region. Microsoft recommends five; three keeps a demo environment cheaper."
  type        = list(string)
  default     = ["apac-sg-sin-azr", "apac-jp-kaw-edge", "emea-au-syd-edge", "emea-nl-ams-azr", "us-va-ash-azr"]
  validation {
    condition = length(var.availability_locations) >= 3 && length(var.availability_locations) <= 16 && length(distinct(var.availability_locations)) == length(var.availability_locations) && alltrue([
      for location in var.availability_locations : contains(["emea-au-syd-edge", "latam-br-gru-edge", "us-fl-mia-edge", "apac-hk-hkn-azr", "us-va-ash-azr", "emea-ch-zrh-edge", "emea-fr-pra-edge", "apac-jp-kaw-edge", "emea-gb-db3-azr", "us-il-ch1-azr", "us-tx-sn1-azr", "apac-sg-sin-azr", "emea-se-sto-edge", "emea-nl-ams-azr", "us-ca-sjc-azr", "emea-ru-msa-edge"], location)
    ])
    error_message = "Choose 3 to 16 distinct supported AzureCloud availability population IDs."
  }
}

variable "availability_failed_location_count" {
  description = "Number of failed locations required for each target alert; default is three of five (use two of three with three locations)."
  type        = number
  default     = 3
  validation {
    condition     = var.availability_failed_location_count >= 2 && var.availability_failed_location_count <= length(var.availability_locations) && floor(var.availability_failed_location_count) == var.availability_failed_location_count
    error_message = "The failure threshold must be an integer from 2 to the configured location count."
  }
}

variable "availability_alert_emails" {
  description = "Team email recipients supplied only in gitignored terraform.tfvars; never commit actual addresses."
  type        = list(string)
  sensitive   = true
  default     = []
  validation {
    condition     = length(var.availability_alert_emails) <= 100 && length(distinct(var.availability_alert_emails)) == length(var.availability_alert_emails) && alltrue([for email in var.availability_alert_emails : can(regex("^[^@[:space:]]+@[^@[:space:]]+\\.[^@[:space:]]+$", email))])
    error_message = "Supply distinct valid email addresses, at most 100."
  }
}

variable "window_guard_enabled" {
  description = "Create the window guard identity that the scheduled window-guard workflow uses to stop resources after a demo window."
  type        = bool
  default     = false
}

variable "window_guard_oidc_credentials" {
  description = "GitHub OIDC credential names and subjects accepted by the window guard identity; use the guard's own GitHub Environment, never the deployment Environment."
  type = map(object({
    name    = string
    subject = string
  }))
  default = {}
}
