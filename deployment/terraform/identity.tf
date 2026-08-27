resource "azurerm_user_assigned_identity" "github_cd" {
  name                = local.github_deployment_identity_name
  location            = azurerm_resource_group.swiftcare.location
  resource_group_name = azurerm_resource_group.swiftcare.name
  tags = merge(local.common_tags, {
    purpose = "github-oidc"
  })
}

resource "azurerm_federated_identity_credential" "github_cd" {
  for_each = var.github_oidc_credentials

  name                      = each.value.name
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_cd.id
  subject                   = each.value.subject
}

resource "azurerm_role_assignment" "github_cd_contributor" {
  scope                = azurerm_resource_group.swiftcare.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.github_cd.principal_id
  principal_type       = "ServicePrincipal"
}
