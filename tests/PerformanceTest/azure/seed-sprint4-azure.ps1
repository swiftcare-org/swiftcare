<#
.SYNOPSIS
    Data seeding for the Sprint 4 samplers - AZURE round (SWC-137).

.DESCRIPTION
    Azure counterpart to ../local/seed-sprint4.ps1 (SWC-136). It writes only under
    tests/PerformanceTest/azure/data/ and shares nothing with the local suite.

    It first runs seed-clinical-flow-azure.ps1 (SWC-127), which creates or reuses the
    Doctor and Receptionist pools, resets any doctor left mid-consultation and checks in
    today's waiting queue. It then uses the bootstrapped administrator to create dedicated
    Admin accounts, each paired with a Receptionist target account that the Admin
    Operations group updates. Only these perf.sprint4.target.* accounts are ever updated;
    the update re-saves the target's own name.

    Writes, all git-ignored:
    * data/sprint4-admins.csv   (username,password,targetId,targetName)
    * data/sprint4-history.csv  (username,password,patientId), one row per seeded patient,
      paired round-robin with the clinic doctors
    * data/sprint4-run.properties  (reportDate, reportMonth for the report samplers)

    -GatewayUrl has no default. SWC-161 separates the development and production
    environments; confirm with DevOps which host is development before seeding, and never
    seed production.

.PARAMETER GatewayUrl
    API Gateway base URL of the development environment, for example https://api-dev.swiftcare.me

.PARAMETER AdminUsername
    Bootstrapped administrator username. Default: admin

.PARAMETER AdminPassword
    Bootstrapped administrator password. Falls back to $env:AZURE_ADMIN_PASSWORD. Never
    hardcode it in this file or commit it.

.PARAMETER DoctorCount
    Doctor accounts for the clinical and patient-history groups. Default: 10

.PARAMETER ReceptionistCount
    Receptionist accounts for the counter and activity-feed groups. Default: 10

.PARAMETER AdminCount
    Admin/target pairs for the report and Admin Operations groups. Default: 4

.PARAMETER QueueVolume
    Patients to check in for today's waiting queue. 0 only resets doctors left
    mid-consultation and reuses the existing data/clinic-patients.csv. Default: 150

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.PARAMETER QueueWaitSeconds
    How long to wait for the deployed QueueService to expose the checked-in patients.
    Default: 180

.EXAMPLE
    # Set AZURE_ADMIN_PASSWORD to the bootstrapped admin password, then, on the day of the run:
    ./seed-sprint4-azure.ps1 -GatewayUrl https://api-dev.swiftcare.me

.EXAMPLE
    # Reset any doctor left mid-consultation, without adding patients:
    ./seed-sprint4-azure.ps1 -GatewayUrl https://api-dev.swiftcare.me -QueueVolume 0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GatewayUrl,
    [string]$AdminUsername = "admin",
    [string]$AdminPassword = $env:AZURE_ADMIN_PASSWORD,
    [int]$DoctorCount = 10,
    [int]$ReceptionistCount = 10,
    [int]$AdminCount = 4,
    [int]$QueueVolume = 150,
    [string]$UserPassword = "LoadTest#Pass1",
    [int]$QueueWaitSeconds = 180
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "No admin password. Pass -AdminPassword or set `$env:AZURE_ADMIN_PASSWORD " +
          "to the bootstrapped administrator's password so this script can log in."
}

$GatewayUrl = $GatewayUrl.TrimEnd('/')
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = Join-Path $scriptDir "data"
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }

function Get-Token([string]$username, [string]$password) {
    $body = @{ username = $username; password = $password } | ConvertTo-Json -Compress
    $resp = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/auth/login" `
        -ContentType "application/json" -Body $body
    return [string]$resp.token
}

function Get-OrCreateUser([string]$adminToken, [hashtable]$account) {
    $headers = @{ Authorization = "Bearer $adminToken" }
    try {
        return Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/users" `
            -Headers $headers -ContentType "application/json" `
            -Body ($account | ConvertTo-Json -Compress)
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -ne 409 -and $status -ne 400) { throw }

        # Assign first, then pipe: @(Invoke-RestMethod ...) keeps the JSON array as one item,
        # so the filter would match the whole list and every userId would be returned.
        $users = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/api/users" -Headers $headers
        $existing = $users | Where-Object { $_.username -eq $account.username } |
            Select-Object -First 1
        if ($null -eq $existing) { throw }
        Write-Host "  $($account.username) already exists - reusing (assumes same password)." -ForegroundColor DarkYellow
        return $existing
    }
}

Write-Host "Target: $GatewayUrl" -ForegroundColor Cyan
Write-Host "Seeding the SWC-127 clinical accounts and queue..." -ForegroundColor Cyan
& (Join-Path $scriptDir "seed-clinical-flow-azure.ps1") `
    -GatewayUrl $GatewayUrl `
    -AdminUsername $AdminUsername `
    -AdminPassword $AdminPassword `
    -DoctorCount $DoctorCount `
    -ReceptionistCount $ReceptionistCount `
    -QueueVolume $QueueVolume `
    -UserPassword $UserPassword

$adminToken = Get-Token $AdminUsername $AdminPassword
if ([string]::IsNullOrWhiteSpace($adminToken)) { throw "Admin login returned no token." }

Write-Host "Creating $AdminCount Sprint 4 Admin/target pairs..." -ForegroundColor Cyan
$adminRows = [System.Collections.Generic.List[string]]::new()
$adminRows.Add("username,password,targetId,targetName")

for ($i = 1; $i -le $AdminCount; $i++) {
    $adminUsername = "perf.sprint4.admin.{0:D3}" -f $i
    $targetUsername = "perf.sprint4.target.{0:D3}" -f $i
    $targetName = "Perf Sprint4 Target $i"

    Get-OrCreateUser $adminToken @{
        username = $adminUsername
        password = $UserPassword
        fullName = "Perf Sprint4 Admin $i"
        role     = "Admin"
    } | Out-Null

    $target = Get-OrCreateUser $adminToken @{
        username = $targetUsername
        password = $UserPassword
        fullName = $targetName
        role     = "Receptionist"
    }
    if ([string]$target.userId -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$') {
        throw "Expected one userId for $targetUsername, got '$($target.userId)'."
    }

    $adminRows.Add("$adminUsername,$UserPassword,$($target.userId),$targetName")
}

Set-Content -Path (Join-Path $dataDir "sprint4-admins.csv") -Value $adminRows -Encoding utf8
Write-Host "  wrote data/sprint4-admins.csv ($($adminRows.Count - 1) rows)" -ForegroundColor Green

$doctorFile = Join-Path $dataDir "clinic-doctors.csv"
$patientFile = Join-Path $dataDir "clinic-patients.csv"
if (-not (Test-Path $patientFile)) {
    throw "data/clinic-patients.csv is missing. Run once with -QueueVolume above 0 to check patients in."
}
$doctorRows = @(Import-Csv -LiteralPath $doctorFile)
$patientRows = @(Import-Csv -LiteralPath $patientFile)
if ($doctorRows.Count -eq 0 -or $patientRows.Count -eq 0) {
    throw "The clinical seed produced no Doctor or patient rows for the history group."
}

$historyRows = [System.Collections.Generic.List[string]]::new()
$historyRows.Add("username,password,patientId")
for ($i = 0; $i -lt $patientRows.Count; $i++) {
    $doctor = $doctorRows[$i % $doctorRows.Count]
    $historyRows.Add("$($doctor.username),$($doctor.password),$($patientRows[$i].patientId)")
}
Set-Content -Path (Join-Path $dataDir "sprint4-history.csv") -Value $historyRows -Encoding utf8
Write-Host "  wrote data/sprint4-history.csv ($($historyRows.Count - 1) rows)" -ForegroundColor Green

if ($QueueVolume -gt 0) {
    $doctorToken = Get-Token $doctorRows[0].username $doctorRows[0].password
    $deadline = (Get-Date).AddSeconds($QueueWaitSeconds)
    do {
        # Direct assignment keeps the returned array's Count; @(...) would wrap it as one item.
        $waiting = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/api/queue/today/waiting" `
            -Headers @{ Authorization = "Bearer $doctorToken" }
        if ($waiting.Count -ge $QueueVolume) { break }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $deadline)

    if ($waiting.Count -lt $QueueVolume) {
        throw "QueueService exposed only $($waiting.Count) of $QueueVolume seeded waiting patients after $QueueWaitSeconds s."
    }
    Write-Host "  today's waiting queue: $($waiting.Count)" -ForegroundColor Green
}

$clinicDate = Get-Date -Format "yyyy-MM-dd"
$clinicMonth = Get-Date -Format "yyyy-MM"
# ASCII, not utf8: Windows PowerShell's utf8 adds a BOM, and JMeter reads .properties as
# ISO-8859-1, so the BOM would rename the first key and reportDate would silently fall back
# to the plan's default date.
Set-Content -Path (Join-Path $dataDir "sprint4-run.properties") -Encoding ascii -Value @(
    "reportDate=$clinicDate"
    "reportMonth=$clinicMonth"
)
Write-Host "  wrote data/sprint4-run.properties (reportDate=$clinicDate reportMonth=$clinicMonth)" -ForegroundColor Green

Write-Host "`nSprint 4 Azure seed complete." -ForegroundColor Green
