<#
.SYNOPSIS
    One-time data seeding for the SwiftCare JMeter load test - AZURE round (SWC-67).

.DESCRIPTION
    Additive Azure counterpart to the local seed.ps1. The local setup under
    tests/PerformanceTest/local/ is untouched. This script writes only under
    tests/PerformanceTest/azure/data/.

    Differences from the local seed.ps1, forced by the deployed environment:

    * Azure runs ASPNETCORE_ENVIRONMENT=Production, so DevelopmentSeeder never
      runs and AUTH_SEED_PASSWORD does not exist there. The seeded accounts
      (admin.fernando, reception.silva, ...) are absent on Azure. This script
      authenticates as the bootstrapped administrator instead.
    * POST /api/patients is Receptionist-only (Admin is NOT allowed). So this
      script uses a two-token flow: the admin token creates the load-user pool,
      then it logs in as the first load user (a Receptionist) and registers the
      seed patients with that token.

    Every registration publishes a patient-checked-in event that the deployed
    QueueService consumes into swiftcare_queue. Seeding PatientCount patients
    therefore also creates ~PatientCount real queue entries on Azure. That is
    expected and harmless; it is called out in TEST-PLAN-AZURE.md.

.PARAMETER GatewayUrl
    API Gateway base URL. Default: https://api.swiftcare.me

.PARAMETER AdminUsername
    Bootstrapped administrator username. Default: admin

.PARAMETER AdminPassword
    Bootstrapped administrator password. Falls back to $env:AZURE_ADMIN_PASSWORD.
    Never hardcode it in this file or commit it.

.PARAMETER UserCount
    Number of load-test staff (Receptionist) accounts to create. Default: 25

.PARAMETER PatientCount
    Number of patient records to register. Default: 500

.PARAMETER DoctorCount
    Number of load-test Doctor accounts to create, each with its own room number, and
    therefore the number of pre-called queue entries written to data/called-queue.csv.
    One pre-called entry buys exactly one consultation (SWC-24), so this sets the size of
    the write pool for the Load run. Default: 70

.PARAMETER QueueSettleSeconds
    Seconds to wait after the last registration before pre-calling, so QueueService has
    consumed the patient-checked-in events. Default: 20

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.EXAMPLE
    # Set AZURE_ADMIN_PASSWORD to the bootstrapped admin password, then run:
    ./seed-azure.ps1

.EXAMPLE
    ./seed-azure.ps1 -PatientCount 500 -UserCount 25
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "https://api.swiftcare.me",
    [string]$AdminUsername = "admin",
    [string]$AdminPassword = $env:AZURE_ADMIN_PASSWORD,
    [int]$UserCount = 25,
    [int]$PatientCount = 500,
    [string]$UserPassword = "LoadTest#Pass1",
    [int]$DoctorCount = 70,
    [int]$QueueSettleSeconds = 20
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "No admin password. Pass -AdminPassword or set `$env:AZURE_ADMIN_PASSWORD " +
          "to the bootstrapped administrator's password so this script can log in."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = Join-Path $scriptDir "data"
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }

function Get-Token([string]$username, [string]$password) {
    $body = @{ username = $username; password = $password } | ConvertTo-Json -Compress
    $resp = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/auth/login" `
        -ContentType "application/json" -Body $body
    return $resp.token
}

# --- Warm-up + auth ----------------------------------------------------------
# The Gateway/Auth/Patient Container Apps scale to zero. The first call after
# idle can take ~20-25 s. Do it here so nothing downstream mistakes a cold
# start for a failure.
Write-Host "Warming the deployment and authenticating as '$AdminUsername'..." -ForegroundColor Cyan
$adminToken = Get-Token $AdminUsername $AdminPassword
if ([string]::IsNullOrWhiteSpace($adminToken)) { throw "Admin login returned no token." }

# --- Staff accounts (created by the admin) ---------------------------------
Write-Host "Creating $UserCount load-test Receptionist accounts..." -ForegroundColor Cyan
$userRows = [System.Collections.Generic.List[string]]::new()
$userRows.Add("username,password")

for ($i = 1; $i -le $UserCount; $i++) {
    $username = "load.user.{0:D3}" -f $i
    $body = @{
        username = $username
        password = $UserPassword
        fullName = "Load User $i"
        role     = "Receptionist"
    } | ConvertTo-Json -Compress

    try {
        Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/users" `
            -Headers @{ Authorization = "Bearer $adminToken" } `
            -ContentType "application/json" -Body $body | Out-Null
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 409 -or $status -eq 400) {
            Write-Host "  $username already exists - reusing (assumes same password)." -ForegroundColor DarkYellow
        }
        else {
            throw
        }
    }
    $userRows.Add("$username,$UserPassword")
}

Set-Content -Path (Join-Path $dataDir "users.csv") -Value $userRows -Encoding utf8
Write-Host "  wrote data/users.csv ($($userRows.Count - 1) rows)" -ForegroundColor Green

# --- Patient records (registered by a Receptionist, NOT the admin) --------
# POST /api/patients rejects Admin. Log in as the first load user for a
# Receptionist token and use it for every registration.
Write-Host "Authenticating as load.user.001 for patient registration..." -ForegroundColor Cyan
$receptionToken = Get-Token "load.user.001" $UserPassword
if ([string]::IsNullOrWhiteSpace($receptionToken)) { throw "load.user.001 login returned no token." }

Write-Host "Registering $PatientCount patients against Azure..." -ForegroundColor Cyan
$stamp = Get-Date -Format "MMddHHmm"
$patientRows = [System.Collections.Generic.List[string]]::new()
$patientRows.Add("patientId")

for ($i = 1; $i -le $PatientCount; $i++) {
    $nic = "$stamp{0:D4}" -f $i                       # 12 digits, unique per run
    $phone = "07{0:D8}" -f ($i % 100000000)           # 0 + 9 digits
    $body = @{
        nic         = $nic
        fullName    = "Perf Patient $stamp-$i"
        dateOfBirth = "2000-01-01"
        gender      = "Male"
        address     = "1 Perf Street, Colombo"
        phoneNumber = $phone
        bloodGroup  = "O+"
    } | ConvertTo-Json -Compress

    try {
        $resp = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/patients" `
            -Headers @{ Authorization = "Bearer $receptionToken" } `
            -ContentType "application/json" -Body $body
        $patientRows.Add($resp.patientId)
    }
    catch {
        Write-Host "  patient $i failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }

    if ($i % 50 -eq 0) { Write-Host "  $i / $PatientCount" -ForegroundColor DarkGray }
}

Set-Content -Path (Join-Path $dataDir "patients.csv") -Value $patientRows -Encoding utf8
Write-Host "  wrote data/patients.csv ($($patientRows.Count - 1) rows)" -ForegroundColor Green

# --- Search terms ----------------------------------------------------------
# Same spread as the local seed: this run's patients, broad name fragments,
# and phone/NIC prefixes - so the search endpoint returns varying result sizes.
$terms = @(
    "term",
    "Perf Patient $stamp",
    "Perf Patient",
    $stamp,
    "Perf",
    "Patient",
    "Silva",
    "Fernando",
    "Perera",
    "077",
    "07",
    "20"
)
Set-Content -Path (Join-Path $dataDir "search-terms.csv") -Value $terms -Encoding utf8
Write-Host "  wrote data/search-terms.csv ($($terms.Count - 1) rows)" -ForegroundColor Green

# --- Doctor accounts + pre-called queue entries (SWC-88) -------------------
# The SWC-24 consultation sampler needs a queue entry that is already
# IN_CONSULTATION, and QueueService allows one open consultation per doctor and
# per room at a time. So each pre-called entry costs one doctor account with its
# own room number, and each entry is good for exactly one 201 (a second
# consultation on the same queueId answers 409). DoctorCount therefore sets the
# size of the write pool for the whole Load run.
Write-Host "`nCreating $DoctorCount load-test Doctor accounts..." -ForegroundColor Cyan
$doctorRows = [System.Collections.Generic.List[string]]::new()
$doctorRows.Add("username,password,roomNumber")

for ($i = 1; $i -le $DoctorCount; $i++) {
    $username = "load.doctor.{0:D3}" -f $i
    $room = "PR{0:D3}" -f $i          # never "1" etc, so real clinic rooms stay free
    $body = @{
        username   = $username
        password   = $UserPassword
        fullName   = "Load Doctor $i"
        role       = "Doctor"
        roomNumber = $room
    } | ConvertTo-Json -Compress

    try {
        Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/users" `
            -Headers @{ Authorization = "Bearer $adminToken" } `
            -ContentType "application/json" -Body $body | Out-Null
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 409 -or $status -eq 400) {
            Write-Host "  $username already exists - reusing (assumes same password and room)." -ForegroundColor DarkYellow
        }
        else {
            throw
        }
    }
    $doctorRows.Add("$username,$UserPassword,$room")
    if ($i % 20 -eq 0) { Write-Host "  $i / $DoctorCount" -ForegroundColor DarkGray }
}

Set-Content -Path (Join-Path $dataDir "doctors.csv") -Value $doctorRows -Encoding utf8
Write-Host "  wrote data/doctors.csv ($($doctorRows.Count - 1) rows)" -ForegroundColor Green

# Registration publishes patient-checked-in over Kafka and QueueService consumes it
# asynchronously, so the waiting pool lags the last registration by a few seconds.
Write-Host "Waiting ${QueueSettleSeconds}s for QueueService to consume the check-in events..." -ForegroundColor Cyan
Start-Sleep -Seconds $QueueSettleSeconds

Write-Host "Pre-calling one patient per doctor for the consultation sampler..." -ForegroundColor Cyan
$calledRows = [System.Collections.Generic.List[string]]::new()
$calledRows.Add("calledQueueId,calledPatientId")

for ($i = 1; $i -le $DoctorCount; $i++) {
    $username = "load.doctor.{0:D3}" -f $i
    try {
        $doctorToken = Get-Token $username $UserPassword
        $called = Invoke-RestMethod -Method Put -Uri "$GatewayUrl/api/queue/call-next" `
            -Headers @{ Authorization = "Bearer $doctorToken" } `
            -ContentType "application/json"
        $calledRows.Add("$($called.queueId),$($called.patientId)")
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 404) {
            Write-Host "  waiting pool is empty after $($calledRows.Count - 1) calls - stopping." -ForegroundColor DarkYellow
            break
        }
        if ($status -eq 409) {
            Write-Host "  $username already has an open consultation - skipping." -ForegroundColor DarkYellow
            continue
        }
        throw
    }
    if ($i % 20 -eq 0) { Write-Host "  $i / $DoctorCount" -ForegroundColor DarkGray }
}

Set-Content -Path (Join-Path $dataDir "called-queue.csv") -Value $calledRows -Encoding utf8
Write-Host "  wrote data/called-queue.csv ($($calledRows.Count - 1) rows)" -ForegroundColor Green

if (($calledRows.Count - 1) -lt $DoctorCount) {
    Write-Host "  fewer pre-called entries than doctors: register more patients (-PatientCount) if the" -ForegroundColor DarkYellow
    Write-Host "  consultation group should stay busy for the whole Load run." -ForegroundColor DarkYellow
}

Write-Host "`nAzure seeding complete. ~$PatientCount patient-checked-in events were" -ForegroundColor Green
Write-Host "published and consumed by the deployed QueueService during this run, and" -ForegroundColor Green
Write-Host "$($calledRows.Count - 1) of those queue entries are now IN_CONSULTATION, held open for the" -ForegroundColor Green
Write-Host "SWC-24 consultation sampler. They stay open: nothing closes a consultation yet." -ForegroundColor Green
