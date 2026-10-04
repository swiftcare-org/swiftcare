# SwiftCare Azure infrastructure

This directory defines stable infrastructure for one Azure development environment. Checked against repository configuration on 2 October 2026; this runbook does not establish live resource state or drift. Import pre-existing resources only when they are absent from the selected state; do not rerun adoption as a routine sprint handover.

## Ownership boundary

Terraform owns stable infrastructure:

- `swiftcare-rg`
- `swiftcare-vnet` and its delegated subnets
- private DNS zones and VNet links
- `swiftcare-mysql` and the AuthService, PatientService, QueueService, MedicalRecordService and PrescriptionService databases
- `swiftcare-logs`
- `swiftcare-appinsights` (Application Insights, workspace-based on `swiftcare-logs`)
- `swiftcare-aca-env`
- the Kafka/ZooKeeper Container Instance, NAT Gateway, public IP and private DNS record
- `swiftcare-web` and its frontend custom domains
- `swiftcare-github-cd`, its federated credentials and scoped Contributor assignment

The CD workflow owns deployable application state:

- Gateway, AuthService, PatientService, QueueService, MedicalRecordService and PrescriptionService Container Apps
- database migration and administrator-bootstrap jobs
- application images, revisions, ingress and secrets
- operational ownership of the gateway; its `api.swiftcare.me` hostname/certificate binding is currently a manual prerequisite, not a CD step

Do not add CD-owned resources to this Terraform state without an explicit ownership decision.
CD reconciles apps/jobs/images/ingress/secrets but has no custom-domain certificate command.
Cloudflare DNS/proxy settings, SQL users/grants, and GitHub Environment/protection settings are also manual.
Terraform defines SWC-132 availability tests, an action group and failure alerts in `availability.tf`; cost budgets and the Notification database remain absent.

## Prerequisites

- Terraform `>= 1.11.0, < 2.0.0`
- AzureRM provider exactly `5.2.0`, locked in `.terraform.lock.hcl`; CI uses Terraform `1.15.8`. Check the actual state writer/version before planning and standardize the operational version with the team.
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

Edit `backend.hcl` with backend subscription/tenant IDs. Edit `terraform.tfvars` with application subscription, allowed region, and verified resource names. Defaults/examples still carry `sprint-1`; choose the agreed tag value in local tfvars rather than assuming it reflects Sprint 4. Enable domains and messaging only when they match the reviewed target/state:

```hcl
frontend_custom_domains_enabled = true
messaging_enabled               = true
sprint_name                     = "sprint-4"
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

The script skips resources already in state and imports only the historical subset it lists. It also selects the CLI subscription. Its coverage is incomplete: Application Insights and the Queue, MedicalRecord, and Prescription databases are omitted. If adoption is needed, review every Terraform resource against state/Azure and separately import the missing existing resources before planning. Do not treat the script's success as a complete adoption check.

The omitted Terraform addresses are `azurerm_application_insights.swiftcare`, `azurerm_mysql_flexible_database.queue`, `azurerm_mysql_flexible_database.medical_record`, and `azurerm_mysql_flexible_database.prescription`. Their Azure IDs are respectively under `Microsoft.Insights/components/<project>-appinsights` and `Microsoft.DBforMySQL/flexibleServers/<server>/databases/<database>`. Obtain the actual IDs read-only and authorize each import. Updating the script is outstanding infrastructure work, not included in this documentation audit.

Verify the result:

```powershell
terraform -chdir=deployment/terraform state list
(terraform -chdir=deployment/terraform state list | Measure-Object -Line).Lines
```

With messaging and both frontend domains enabled and the default two OIDC credentials, the configuration expands to 36 managed resource instances, including all five databases and the five availability monitoring resources. Counts vary with switches/credential inputs and do not prove the resources exist in state. Check addresses individually. `medical_record` has a hardcoded `swiftcare_medical_record` name in `database.tf`, so changing `project_name` alone does not isolate every database name.

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

Saved plans and state are ignored and must be handled as sensitive artifacts. Only the MySQL `administrator_password_wo` resource argument is write-only; the input variable is sensitive but not declared ephemeral, so do not infer that saved plans cannot contain the supplied password. Provider registration is `none`; required Azure providers must be registered beforehand by an authorized administrator.

## Application Insights

Terraform creates `swiftcare-appinsights` and exposes a sensitive `application_insights_connection_string` output. The resource providers `Microsoft.Insights` and `Microsoft.AlertsManagement` must already be registered on the subscription, which needs the subscription Owner once.

CD passes the connection string to every Container App as `APPLICATIONINSIGHTS_CONNECTION_STRING`. It is optional, so deployments keep working until it is configured. After the first apply, store it in the GitHub Environment:

```powershell
terraform -chdir=deployment/terraform output -raw application_insights_connection_string | gh secret set APPLICATIONINSIGHTS_CONNECTION_STRING --env azure-development
```

Keep the connection string out of the repository and chat.
Inspect any historical trial resource/alert and dependencies before arranging an authorized cleanup; do not assume it still exists.
Every application has optional Azure Monitor OpenTelemetry registration and a distinct role name.
`/health` is liveness only, without MySQL/Kafka checks.

### SWC-132 availability tests and alerts

`availability.tf` defines separate standard tests for `https://swiftcare.me` and `https://api.swiftcare.me/health`, linked to this Application Insights resource.
AzureRM creates the required hidden-link tag from `application_insights_id`; declare only ordinary project tags so refresh does not propose repeated hidden-link tag updates.
Both run every five minutes from Southeast Asia, Japan East, Australia East, West Europe and East US, using retries, TLS validation, a 30-second timeout and exact HTTP 200 without redirects or dependent requests.
Each target has a severity-1 alert when at least three locations fail, evaluated every minute over a five-minute window, with automatic resolution and the shared email action group.
Set `availability_alert_emails` only in gitignored `terraform.tfvars`; the sensitive list defaults empty and enabled monitoring refuses to plan without recipients.
Change notification targets by updating that local input and reviewing/applying a new saved plan; do not edit receivers manually in Azure.
Recipients must complete Azure's email verification when required; an Enabled receiver can still be VerificationPending and unable to receive alerts.
Complete recipient verification privately through the action group's Notifications page, using Resend for expired codes; keep passcodes out of Git, commands and evidence.
Recipients may remain in state/plans even though marked sensitive, so never publish full plan/state output.
For another environment, configure `availability_frontend_url` and `availability_gateway_url` to its deployed public origins before applying.
`availability_enabled = false` retains the resources but disables both tests and alerts during agreed maintenance; restore it through a reviewed plan after maintenance.
Standard tests incur charges, and the gateway must remain running throughout testing and monitoring.
These checks prove frontend response and gateway process liveness, not clinical journeys or dependency readiness.

Run `fmt -check -recursive`, `validate`, and `plan -input=false -detailed-exitcode -out=swc-132.tfplan` using the verified backend and operator-supplied MySQL input.
Against a clean existing baseline, review only five new resources: two tests, two alerts and one action group.
Stop on unintended changes and use `apply -input=false swc-132.tfplan` only after exact reviewed-plan authorization.
Live verification on 3 October 2026: the authorized saved plan created all five monitoring resources, and both tests produced passing samples from every configured location.
The controlled gateway test-URL drill produced failures from all five locations and a Fired alert with an ActionsTriggered history event; the test URL was restored to `/health` through a separately reviewed plan.
The post-recovery convergence plan returned no changes after correcting redundant hidden-link tag configuration; later private recipient changes require a separate reviewed action-group plan and a fresh convergence check after applying it.
The gateway alert resolved at 00:24:50 Asia/Colombo on 3 October, with a second ActionsTriggered event at 00:24:51; both targets then passed from all five locations.
The later comparison confirmed delivered Fired and Resolved school-mail notifications; private Portal screenshots show both tests at 100% and each passing from all five locations during 3 October 01:20-01:30 Asia/Colombo.
PR review/merge and isolated new-environment recreation remain outstanding before marking the story Done.
Delivery investigation found one Verified receiver and three VerificationPending receivers through the 2026-03-01-preview read API; the older read API reports only Enabled status.
The authorized action-group notification test was rejected with HTTP 409, Conflict, Free subscription not supported; this blocks the standalone test operation and does not prove live-alert email is unsupported.
The subsequent authorized comparison confirmed Fired and Resolved email receipt in the school mailbox, with both targets passing from all five locations before shutdown; personal mailbox delivery remains unconfirmed.
The 3 October shutdown encountered backend DNS errors followed by a stale Terraform planning lock; its original 15-minute deadline was missed.
On 4 October the operator explicitly authorized the checked stale-lock release and reviewed maintenance plan.
The apply completed with 0 added, 4 changed and 0 destroyed; read-back confirmed both tests and both alerts disabled, all six apps Stopped and backend lease unlocked.
The operator's fresh private-input post-maintenance plan on 4 October validated successfully and returned exit code 0 with zero resource actions; the reviewed saved-plan SHA256 is DC1C23C47DB211AB5292AE707914C622CFEE18CB593BA37CD9B20FE604D1027B.
The shared environment now follows the approved demo/test windows below; do not re-enable monitoring until the intended targets are running and healthy.
The 4 October read-only Cloudflare zone review found one rate-limit rule scoped only to `/api/auth/login`, no custom WAF or zone IP/User Agent/lockdown rules, Bot Fight Mode and JavaScript challenge off, and Access disabled.
Cloudflare's Free Managed Ruleset remains present; both targets passed from all five Azure test locations during the authorized ON window, confirming the tested requests were allowed then.
No Cloudflare rule was changed; future rule changes and account-wide IP access settings require a fresh check.
The following procedure is sufficient for deployment and evidence collection; the private DevOps runbook records the current operator evidence separately.
Cloudflare must permit current `ApplicationInsightsAvailability` agent CIDRs on the exact hosts/paths without challenges or rate limiting; ordinary curl success is insufficient evidence.
Capture both tests passing from every configured location, Availability screenshots, sanitized Cloudflare policy/traffic evidence, and the Fired/Resolved alert plus delivered team notification after an authorized drill.
Keep the gateway running during that drill; change only the test URL through Terraform and restore it through a new reviewed plan.

#### Deployment prerequisites and saved plan

Confirm the explicit subscription, tenant, backend and state version before planning.
The operator needs backend blob/lock access and permission to create Microsoft.Insights web tests, action groups and metric alerts; Microsoft.Insights and Microsoft.AlertsManagement must already be registered and allowed by subscription policy.
Agree the team recipients, monitoring cost, gateway uptime window and maintenance owner.
Confirm the gateway is Running with its latest ready revision and public `/health` returns HTTP 200; do not stop it during sampling, failure testing or recovery.
Use the existing initialized backend; initialize with the documented backend configuration only when needed.
Supply the existing MySQL password privately through `TF_VAR_mysql_administrator_password`, and enter recipients directly in local `terraform.tfvars` without displaying either value.

```powershell
git check-ignore deployment/terraform/terraform.tfvars
git ls-files -- deployment/terraform/terraform.tfvars
terraform -chdir=deployment/terraform fmt -check -recursive
terraform -chdir=deployment/terraform validate
terraform -chdir=deployment/terraform plan -input=false -detailed-exitcode -out=swc-132.tfplan
```

The tracked-file check must return no output.
Plan exit 2 means changes, exit 0 means no changes, and exit 1 means failure.
Review the saved plan privately and summarize only resource addresses/actions; do not publish full plan output, JSON or recipient values.
Require only the five monitoring creates on the existing clean baseline, with no application, database, network or identity changes.
After authorization for that reviewed plan, apply and run a new plan to verify convergence:

```powershell
terraform -chdir=deployment/terraform apply -input=false swc-132.tfplan
terraform -chdir=deployment/terraform plan -input=false -detailed-exitcode -out=swc-132-post-apply.tfplan
Remove-Item Env:\TF_VAR_mysql_administrator_password
```

Keep all saved plans ignored and private.
Require post-apply exit 0; regenerate and review plans after input/state changes rather than reusing stale plans.

#### Cloudflare agents and evidence

Obtain the current Azure test-agent address prefixes read-only:

```powershell
az network list-service-tags --subscription 0f585319-2092-4ed0-993a-b30886e4e07c --location eastasia --query "values[?name=='ApplicationInsightsAvailability'].properties.addressPrefixes" --output json
```

The Cloudflare owner checks custom/managed WAF rules, rate limits, bot protection, challenges, IP/country restrictions and Access for GET on both exact target hosts/paths.
If agents already pass, preserve the existing rules.
If an exception is necessary, review a condition combining current agent CIDRs, exact host/path and GET, and authorize only the specific blocking products/rules.
Cloudflare cannot consume Azure service tags directly; record who maintains the IP list when Microsoft updates it.
Skip does not bypass ordinary Bot Fight Mode, and shared Azure agent addresses do not authenticate requests.
Preserve application authorization and verify Full (strict), DNS targets, proxy flags and certificates separately.
Correlate Azure test timestamps/locations with sanitized Cloudflare policy and traffic evidence; missing sampled Security Events or a successful local curl alone does not establish agent access.

After propagation and several five-minute cycles, capture the Application Insights Availability view showing both test names and passing results from all five locations in a recorded UTC window.
Use aggregate workspace evidence without request bodies or patient data:

```kusto
AppAvailabilityResults
| where TimeGenerated > ago(1h)
| where Name in ('swiftcare-frontend-availability', 'swiftcare-gateway-availability')
| summarize Samples=count(), Passed=countif(Success == true), Failed=countif(Success == false), LastSample=max(TimeGenerated) by Name, Location
| order by Name asc, Location asc
```

Require recent passing samples for each target/location; absent samples do not pass acceptance.
Verify deployed alert test/component IDs, threshold, enabled state and action-group reference without displaying receiver addresses.
For a separately authorized drill, first confirm an agreed same-origin invalid path returns a non-200 response, then temporarily change only that test URL in local tfvars.
Save/review/apply a new plan containing only that test URL update; keep the actual gateway `/health` endpoint running and unchanged.
Record failures from at least three locations, the Fired alert ID/time and actual team notification receipt with addresses/headers redacted.
Restore the original target at the drill deadline even if delivery fails, using a newly reviewed recovery plan.
Require fresh passing results across all locations, Resolved state, recovery notification and a no-change plan.
Prove both rules if required; frontend unknown paths may return HTTP 200 because of the SPA fallback, so verify the drill response before choosing a path.
Rollback restores the reviewed inputs or disables tests and alerts with `availability_enabled = false` during agreed maintenance; it does not remove telemetry or application resources.
Retain sanitized Availability screenshots, location aggregates, plan/apply action summaries, alert/notification proof, Cloudflare evidence and reviewed PR/merge/CI references in the Sprint 4 report.

References: [Microsoft availability tests and agent firewall guidance](https://learn.microsoft.com/en-us/azure/azure-monitor/app/availability), [AzureRM 5.2.0 alert criteria](https://github.com/hashicorp/terraform-provider-azurerm/blob/v5.2.0/website/docs/r/monitor_metric_alert.html.markdown), and [Cloudflare Skip limitations](https://developers.cloudflare.com/waf/custom-rules/skip/).

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

MySQL runtime start/stop are operational actions rather than infrastructure changes. CD sets `min-replicas=1` and `max-replicas=1` for all five services and the gateway. Stopping apps does not stop MySQL, Kafka, or NAT charges. Removing messaging destroys ephemeral broker data/offsets; there is no volume or explicit Azure topic-provisioning step, so coordinate data loss and topic/consumer recovery before approving the plan.

For an approved shutdown, ensure no CD deployment is running/queued, pause the availability tests and alerts through a reviewed Terraform maintenance plan, stop all six application Container Apps (including Prescription), and verify `Stopped`.
Any messaging removal and MySQL shutdown require separate approval; removing messaging destroys broker data and offsets.
Retained storage/other resources can still incur charges.
Document who approved the window and the restart owner.

For startup, verify MySQL is ready and messaging/DNS/broker state is correct before deploying the reviewed commit. CD explicitly starts stopped Auth/Patient/Queue/MedicalRecord/Gateway apps but lacks the Prescription start/readiness step; an operator must verify Prescription is running and arrange an authorized start if needed. Check all six app states, revisions and images, custom-domain health, role-specific clinical routing, Kafka flow, and frontend behavior. CD's current smoke test covers neither MedicalRecord nor Prescription API routing, and image rollback does not undo migrations or configuration.

### Approved demo and test windows

The shared development apps run only for an approved demo or test, then return to `Stopped`.
Starting an existing app resumes its deployed image; it does not deploy a new release.
Record each window's start/end time in Asia/Colombo, responsible operator and cost owner before startup.
Check no CD run is active or queued, dependencies are ready, all six apps have ready replicas and frontend/gateway health return HTTP 200 before enabling monitoring.
Use a reviewed saved Terraform plan with `availability_enabled=true` to resume the two tests and two alerts during the approved window.
Before the window ends, apply a reviewed saved plan with `availability_enabled=false`, verify both tests and both alerts report disabled, then stop and verify all six apps.
The disabled resources remain provisioned for the next window; the action group and telemetry are retained.
This schedule is an operator procedure, not an implemented automatic scheduler.
Changing backend locks requires a separately authorized checked lock ID; never bypass locking with `-lock=false`.

### Cloudflare change and verification

Dhananjaya owns the `swiftcare.me` Cloudflare zone.
The agreed target is minimum TLS 1.2, Full (strict), and Always Use HTTPS after checking the existing redirect chain.
Before changing Full (strict), verify trusted, unexpired origin certificates for `swiftcare.me`, `www.swiftcare.me` and `api.swiftcare.me` using their actual hostnames and SNI.
The frontend origin is `white-island-09489d800.6.azurestaticapps.net`; the API origin is `swiftcare-gateway.yellowfield-6231b42e.eastasia.azurecontainerapps.io`.
Recheck these origin values against Azure before each change and preserve the current DNS records and proxy flags.
Record the previous encryption mode, automatic-mode setting, minimum TLS and HTTPS redirect setting, then change one setting at a time and verify apex/www/api after each change.
In the approved ON window, expect frontend and gateway health HTTP 200, valid certificates and HTTP redirects that end at HTTPS without a loop.
A stopped API can return 404 even when TLS verification succeeds; do not call that a successful API availability check.
If a change breaks certificate validation or introduces a redirect loop, restore only the setting just changed and repeat verification.
Dashboard access and final read-back are required; public HTTPS checks alone do not prove Cloudflare policy settings.
See [Cloudflare Full (strict)](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes/full-strict/), [minimum TLS](https://developers.cloudflare.com/ssl/edge-certificates/additional-options/minimum-tls/) and [HTTPS redirect guidance](https://developers.cloudflare.com/ssl/edge-certificates/encrypt-visitor-traffic/).

## Sprint handover

Role rotation within the existing subscription does not require a new environment. First confirm access, state/backend location, secret custody, running schedule, registry token ownership/expiry, GitHub release approvals, Cloudflare ownership, and a reviewed baseline plan. Do not copy state or change its subscription merely to change the sprint owner.

If the team chooses to recreate the environment in another student subscription, that is a separately approved migration with its own backend/state and cost window:

1. Select a region available to their subscription.
2. Create a separate Terraform backend in that subscription.
3. Copy the example configuration and replace subscription, tenant, region and globally unique names.
4. Run a reviewed plan to create the stable infrastructure.
5. Configure the new GitHub Environment values from Terraform outputs.
6. Move custom DNS records only after the new endpoints exist.
7. Run CD to create and deploy the application resources.
8. Verify the replacement environment before deleting the previous one.

The repository has no automated cross-subscription migration. Treat the list above as a recreation procedure; any alternative resource-move strategy needs a separate supported-resource assessment. Never delete the previous environment before data recovery, DNS, credentials, and the replacement deployment are verified.
