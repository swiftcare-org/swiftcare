# SwiftCare Azure infrastructure

This directory defines the stable Azure development infrastructure with Terraform. Existing Sprint 1 resources must be imported before the first plan is applied.

## Ownership boundary

Terraform owns stable infrastructure:

- `swiftcare-rg`
- `swiftcare-vnet` and its delegated subnets
- private DNS zones and VNet links
- `swiftcare-mysql` and the AuthService, PatientService and QueueService databases
- `swiftcare-logs`
- `swiftcare-aca-env`
- the Kafka/ZooKeeper Container Instance, NAT Gateway, public IP and private DNS record
- `swiftcare-web` and its frontend custom domains
- `swiftcare-github-cd`, its federated credentials and scoped Contributor assignment

The CD workflow owns deployable application state:

- Gateway, AuthService, PatientService and QueueService Container Apps
- database migration and administrator-bootstrap jobs
- application images, revisions, ingress and secrets
- the `api.swiftcare.me` Gateway binding and its managed certificate

Do not add CD-owned resources to this Terraform state. Their configuration changes with every deployment and is already reconciled by `.github/workflows/cd.yml`.

## Prerequisites

- Terraform `>= 1.11.0, < 2.0.0`
- Azure CLI authenticated to the target student subscription
- Git Bash on Windows for the import script
- Owner or equivalent permissions for role-assignment management
- `Storage Blob Data Contributor` on the Terraform backend Storage Account

Check the active account before every operation:

```powershell
az account show --query "{subscription:name,subscriptionId:id,tenantId:tenantId}" --output table
```

## State backend

State is stored separately from the application resource group:

```text
swiftcare-tfstate-rg
`-- swiftcaretfstate
    `-- tfstate/azure-development.tfstate
```

Keeping state outside `swiftcare-rg` allows the application environment to be recreated without deleting the record Terraform needs to manage it.

The backend must use HTTPS, TLS 1.2, private blob access, Azure AD authorization, blob versioning and soft-delete retention. It is created once per student subscription before Terraform initialization.

## Local configuration

Copy the committed templates:

```powershell
Copy-Item deployment/terraform/backend.hcl.example deployment/terraform/backend.hcl
Copy-Item deployment/terraform/terraform.tfvars.example deployment/terraform/terraform.tfvars
```

Edit `backend.hcl` with the backend subscription and tenant IDs. Edit `terraform.tfvars` with the application subscription, selected Azure region and existing resource names. For the current environment, custom domains and messaging both exist, so set:

```hcl
frontend_custom_domains_enabled = true
messaging_enabled               = true
```

Both real files are ignored by Git. Never add passwords to either file.

## Initialize the backend

From the repository root:

```powershell
terraform -chdir=deployment/terraform init -reconfigure -backend-config="backend.hcl"
terraform -chdir=deployment/terraform validate
```

Use `-backend-config="backend.hcl"` whenever backend initialization is required. A plain `terraform init` cannot populate the partial backend and will prompt for missing values.

## Supply the MySQL administrator password

The existing MySQL administrator password is required for import, refresh, plan and apply because Azure treats it as a write-only server argument. It is not an AuthService, PatientService or QueueService database password.

Set it without displaying it:

```powershell
$mysqlAdminSecure = Read-Host "Existing swiftcareadmin MySQL password" -AsSecureString
$env:TF_VAR_mysql_administrator_password = [Net.NetworkCredential]::new("", $mysqlAdminSecure).Password
```

Remove it from the shell when Terraform work is complete:

```powershell
Remove-Item Env:\TF_VAR_mysql_administrator_password
Remove-Variable mysqlAdminSecure
```

## Adopt the existing environment

Set the import inputs in the same PowerShell session:

```powershell
$env:SUBSCRIPTION_ID = az account show --query id --output tsv
$env:RESOURCE_GROUP = "swiftcare-rg"
$env:PROJECT_NAME = "swiftcare"
$env:MYSQL_SERVER_NAME = "swiftcare-mysql"
$env:STATIC_WEB_APP_NAME = "swiftcare-web"
$env:DEPLOYMENT_IDENTITY_NAME = "swiftcare-github-cd"
$env:FRONTEND_DOMAIN = "swiftcare.me"
$env:FRONTEND_WWW_DOMAIN = "www.swiftcare.me"
$env:IMPORT_CUSTOM_DOMAINS = "true"
$env:IMPORT_MESSAGING = "true"
```

Run the import script from the Terraform directory so its `terraform` commands use the correct configuration:

```powershell
Push-Location deployment/terraform
& "C:\Program Files\Git\bin\bash.exe" "./scripts/import-existing.sh"
Pop-Location
```

The script is idempotent. If an import fails after several resources succeed, fix the reported issue and run it again; resources already in state are skipped.

Verify the result:

```powershell
terraform -chdir=deployment/terraform state list
(terraform -chdir=deployment/terraform state list | Measure-Object -Line).Lines
```

With messaging, all three Sprint 1 databases, and both frontend custom domains enabled, the current environment contains approximately 29 managed resource addresses.

## Review before applying

Never apply immediately after import. Save and inspect a plan:

```powershell
terraform -chdir=deployment/terraform plan -out="azure-development.tfplan"
terraform -chdir=deployment/terraform show "azure-development.tfplan"
```

Stop and investigate if the plan proposes:

- destroying or replacing MySQL, any service database, the VNet or Container Apps environment
- recreating validated custom domains
- changing subnet prefixes or delegations
- replacing the deployment identity
- managing application Container Apps or jobs

Apply only the exact reviewed plan:

```powershell
terraform -chdir=deployment/terraform apply "azure-development.tfplan"
```

Saved plan files are ignored by Git.

## Cost controls

The paid messaging layer is ephemeral. To remove its Container Instance, NAT Gateway, public IP and Kafka DNS record while retaining the VNet, delegated subnet and private DNS zone:

```powershell
terraform -chdir=deployment/terraform plan -var="messaging_enabled=false" -out="messaging-off.tfplan"
terraform -chdir=deployment/terraform show "messaging-off.tfplan"
terraform -chdir=deployment/terraform apply "messaging-off.tfplan"
```

To recreate it:

```powershell
terraform -chdir=deployment/terraform plan -var="messaging_enabled=true" -out="messaging-on.tfplan"
terraform -chdir=deployment/terraform show "messaging-on.tfplan"
terraform -chdir=deployment/terraform apply "messaging-on.tfplan"
```

Terraform destroys and recreates these resources; it does not pause them. The new private IP is written automatically to the Kafka private DNS record.

MySQL runtime start and stop are operational actions rather than desired infrastructure changes. Use Azure CLI for those actions. Container Apps use `min-replicas=0` through CD and scale to zero when idle.

## Sprint handover

Each student subscription needs its own backend and state. A new Sprint DevOps owner should:

1. Select a region available to their subscription.
2. Create a separate Terraform backend in that subscription.
3. Copy the example configuration and replace subscription, tenant, region and globally unique names.
4. Run a reviewed plan to create the stable infrastructure.
5. Configure the new GitHub Environment values from Terraform outputs.
6. Move custom DNS records only after the new endpoints exist.
7. Run CD to create and deploy the application resources.
8. Verify the replacement environment before deleting the previous one.

Azure resources cannot be transferred between student subscriptions merely by changing their owner. A new environment must be provisioned in the next subscription.
