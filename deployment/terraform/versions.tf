terraform {
  required_version = ">= 1.11.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 5.2.0"
    }
  }

  # Supply the Azure Storage settings at init time with backend.hcl. Keeping
  # this block partial prevents subscription-specific values being committed.
  backend "azurerm" {}
}
