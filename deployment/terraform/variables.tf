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
  description = "Delegated subnet used by the Kafka and ZooKeeper container group."
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
  description = "Increment only when rotating the write-only MySQL administrator password."
  type        = number
  default     = 1
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

variable "frontend_custom_domains_enabled" {
  description = "Manage the existing frontend custom domains after DNS validation."
  type        = bool
  default     = false
}

variable "frontend_domain" {
  description = "Apex custom domain attached to the Static Web App."
  type        = string
  default     = "swiftcare.me"
}

variable "frontend_www_domain" {
  description = "WWW custom domain attached to the Static Web App."
  type        = string
  default     = "www.swiftcare.me"
}

variable "messaging_enabled" {
  description = "Whether the paid NAT Gateway and Kafka/ZooKeeper container group exist."
  type        = bool
  default     = true
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

variable "additional_tags" {
  description = "Optional tags merged with the standard project and environment tags."
  type        = map(string)
  default     = {}
}
