<#
.SYNOPSIS
    One-time data seeding for the Sprint 3 clinical-flow samplers - AZURE round (SWC-127).

.DESCRIPTION
    Azure counterpart to ../local/seed-clinical-flow.ps1 (SWC-126). It writes only under
    tests/PerformanceTest/azure/data/ and shares nothing with the local suite.

    The clinical-flow Thread Groups in swiftcare-load-azure.jmx need a Doctor pool, a
    Receptionist pool and a waiting queue deep enough that no doctor thread runs out of
    patients during the run. Each doctor thread repeats call-next -> consultation ->
    vitals -> complete -> prescription. Call-next allows one open consultation per doctor
    and per room, so each doctor gets its own room and the plan never shares a doctor
    account between threads.

    Differences from the local seed, forced by the deployed environment (see seed-azure.ps1):

    * Azure has no development-seeded accounts. The bootstrapped administrator creates the
      pools, and the first seeded Receptionist registers the patients, because
      POST /api/patients rejects Admin.
    * Registration publishes patient-checked-in, which the deployed QueueService turns into
      a today-dated Waiting entry. Each doctor cycle consumes one. There is no reset
      equivalent on Azure, so every seeded row stays in the deployed databases.

    A run stopped part-way can leave a doctor holding a called patient, which makes every
    later call-next for that doctor answer 409. Before writing the CSVs, this script
    finishes any such consultation through the real APIs (consultation, vitals, complete).
    Re-running it therefore resets the doctor pool.

    Writes data/clinic-doctors.csv and data/clinic-receptionists.csv (username,password),
    git-ignored.

.PARAMETER GatewayUrl
    API Gateway base URL. Default: https://api.swiftcare.me

.PARAMETER AdminUsername
    Bootstrapped administrator username. Default: admin

.PARAMETER AdminPassword
    Bootstrapped administrator password. Falls back to $env:AZURE_ADMIN_PASSWORD. Never
    hardcode it in this file or commit it.

.PARAMETER DoctorCount
    Number of load-test Doctor accounts to create. Default: 10

.PARAMETER ReceptionistCount
    Number of load-test Receptionist accounts to create. Default: 10

.PARAMETER QueueVolume
    Number of patients to check in, each producing one today-dated Waiting entry for a
    doctor cycle to consume. Default: 200

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.EXAMPLE
    # Set AZURE_ADMIN_PASSWORD to the bootstrapped admin password, then run:
    ./seed-clinical-flow-azure.ps1

.EXAMPLE
    # Reset any doctor left mid-consultation, without adding patients:
    ./seed-clinical-flow-azure.ps1 -QueueVolume 0
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "https://api.swiftcare.me",
    [string]$AdminUsername = "admin",
    [string]$AdminPassword = $env:AZURE_ADMIN_PASSWORD,
    [int]$DoctorCount = 10,
    [int]$ReceptionistCount = 10,
    [int]$QueueVolume = 200,
    [string]$UserPassword = "LoadTest#Pass1"
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

function New-StaffAccount([string]$adminToken, [hashtable]$account) {
    try {
        Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/users" `
            -Headers @{ Authorization = "Bearer $adminToken" } `
            -ContentType "application/json" -Body ($account | ConvertTo-Json -Compress) | Out-Null
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 409 -or $status -eq 400) {
            Write-Host "  $($account.username) already exists - reusing (assumes same password)." -ForegroundColor DarkYellow
        }
        else {
            throw
        }
    }
}

# Finishes a consultation left open by an interrupted run, so call-next works again.
function Complete-OpenConsultation([string]$username, [string]$token) {
    $headers = @{ Authorization = "Bearer $token" }
    $current = Invoke-WebRequest -Method Get -Uri "$GatewayUrl/api/queue/today/current" `
        -Headers $headers -UseBasicParsing
    if ($current.StatusCode -eq 204) { return }

    $called = $current.Content | ConvertFrom-Json
    Write-Host "  $username still holds $($called.queueNumber) - completing it." -ForegroundColor DarkYellow

    $progress = Invoke-WebRequest -Method Get `
        -Uri "$GatewayUrl/api/consultations/by-queue/$($called.queueId)" `
        -Headers $headers -UseBasicParsing
    if ($progress.StatusCode -eq 204) {
        $consultation = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/consultations" `
            -Headers $headers -ContentType "application/json" -Body (@{
                queueId   = $called.queueId
                patientId = $called.patientId
                symptoms  = "Perf seed reset"
                diagnosis = "Perf seed reset"
            } | ConvertTo-Json -Compress)
        $consultationId = $consultation.id
        $hasVitals = $false
    }
    else {
        $state = $progress.Content | ConvertFrom-Json
        $consultationId = $state.id
        $hasVitals = $state.hasVitalSigns
    }

    if (-not $hasVitals) {
        Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/consultations/$consultationId/vitals" `
            -Headers $headers -ContentType "application/json" `
            -Body (@{ temperatureCelsius = 37 } | ConvertTo-Json -Compress) | Out-Null
    }

    Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/consultations/$consultationId/complete" `
        -Headers $headers | Out-Null
}

# --- Auth ------------------------------------------------------------------------
Write-Host "Authenticating as '$AdminUsername'..." -ForegroundColor Cyan
$adminToken = Get-Token $AdminUsername $AdminPassword
if ([string]::IsNullOrWhiteSpace($adminToken)) { throw "Admin login returned no token." }

# --- Doctor accounts, one room each ----------------------------------------------
Write-Host "Creating $DoctorCount load-test Doctor accounts..." -ForegroundColor Cyan
$doctorRows = [System.Collections.Generic.List[string]]::new()
$doctorRows.Add("username,password")

for ($i = 1; $i -le $DoctorCount; $i++) {
    $username = "perf.clinic.doctor.{0:D3}" -f $i
    New-StaffAccount $adminToken @{
        username   = $username
        password   = $UserPassword
        fullName   = "Perf Clinic Doctor $i"
        role       = "Doctor"
        roomNumber = "PC-{0:D3}" -f $i
    }
    Complete-OpenConsultation $username (Get-Token $username $UserPassword)
    $doctorRows.Add("$username,$UserPassword")
}

Set-Content -Path (Join-Path $dataDir "clinic-doctors.csv") -Value $doctorRows -Encoding utf8
Write-Host "  wrote data/clinic-doctors.csv ($($doctorRows.Count - 1) rows)" -ForegroundColor Green

# --- Receptionist accounts -------------------------------------------------------
Write-Host "Creating $ReceptionistCount load-test Receptionist accounts..." -ForegroundColor Cyan
$receptionistRows = [System.Collections.Generic.List[string]]::new()
$receptionistRows.Add("username,password")

for ($i = 1; $i -le $ReceptionistCount; $i++) {
    $username = "perf.clinic.reception.{0:D3}" -f $i
    New-StaffAccount $adminToken @{
        username = $username
        password = $UserPassword
        fullName = "Perf Clinic Reception $i"
        role     = "Receptionist"
    }
    $receptionistRows.Add("$username,$UserPassword")
}

Set-Content -Path (Join-Path $dataDir "clinic-receptionists.csv") -Value $receptionistRows -Encoding utf8
Write-Host "  wrote data/clinic-receptionists.csv ($($receptionistRows.Count - 1) rows)" -ForegroundColor Green

# --- Waiting queue: one check-in per doctor cycle --------------------------------
# POST /api/patients rejects Admin, so the first seeded Receptionist registers them.
if ($QueueVolume -gt 0) {
    $receptionToken = Get-Token "perf.clinic.reception.001" $UserPassword
    if ([string]::IsNullOrWhiteSpace($receptionToken)) { throw "perf.clinic.reception.001 login returned no token." }

    Write-Host "Checking in $QueueVolume patients to seed today's waiting queue..." -ForegroundColor Cyan
    $stamp = Get-Date -Format "MMddHHmm"
    $checkedIn = 0

    for ($i = 1; $i -le $QueueVolume; $i++) {
        $nic = "$stamp{0:D4}" -f $i
        $phone = "07{0:D8}" -f ($i % 100000000)
        $body = @{
            nic         = $nic
            fullName    = "Clinic Perf Patient $stamp-$i"
            dateOfBirth = "2000-01-01"
            gender      = "Female"
            address     = "1 Perf Street, Colombo"
            phoneNumber = $phone
            bloodGroup  = "A+"
        } | ConvertTo-Json -Compress

        try {
            Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/patients" `
                -Headers @{ Authorization = "Bearer $receptionToken" } `
                -ContentType "application/json" -Body $body | Out-Null
            $checkedIn++
        }
        catch {
            Write-Host "  patient $i failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
        }

        if ($i % 50 -eq 0) { Write-Host "  $i / $QueueVolume" -ForegroundColor DarkGray }
    }

    Write-Host "  checked in $checkedIn / $QueueVolume patients" -ForegroundColor Green
}

Write-Host "`nClinical-flow seed complete. Give the deployed QueueService a few seconds to" -ForegroundColor Green
Write-Host "drain the patient-checked-in topic before starting a run." -ForegroundColor Green
