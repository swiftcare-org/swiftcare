<#
.SYNOPSIS
    One-time data seeding for the Sprint 3 clinical-flow JMeter suite.

.DESCRIPTION
    Seeds what SWC-126-clinical-flow.jmx needs: a Doctor account pool, a Receptionist
    account pool and a waiting queue deep enough that no doctor thread runs out of
    patients during a run.

    Every doctor thread in the plan logs in as its own account and repeats
    call-next -> consultation -> vitals -> complete -> prescription. Call-next
    allows one open consultation per doctor and per room, so each account gets its
    own room and the plan never shares an account between threads. Seed at least as
    many doctors as the largest doctor thread count you intend to run.

    Queue entries are not created directly - QueueService only gets them from the
    patient-checked-in Kafka event PatientService publishes on registration, so this
    script registers patients through the gateway and lets the existing consumer
    turn them into today-dated Waiting entries. Each doctor cycle consumes one.

    A run stopped part-way can leave a doctor holding a called patient, which makes
    every later call-next for that doctor answer 409. Before writing the CSVs, this
    script finishes any such consultation through the real APIs (consultation,
    vitals, complete), so re-running it resets the doctor pool.

    Writes data/clinic-doctors.csv and data/clinic-receptionists.csv
    (username,password), git-ignored.

.PARAMETER GatewayUrl
    API Gateway base URL. Default: http://localhost:8000

.PARAMETER DoctorCount
    Number of load-test Doctor accounts to create. Default: 10

.PARAMETER ReceptionistCount
    Number of load-test Receptionist accounts to create. Default: 10

.PARAMETER QueueVolume
    Number of patients to check in, each producing one today-dated Waiting queue
    entry for a doctor cycle to consume. Default: 300

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.EXAMPLE
    # Set AUTH_SEED_PASSWORD to the value in the repo-root .env, then run:
    ./seed-clinical-flow.ps1

.EXAMPLE
    # Stress: more doctors than the peak doctor thread count, and a deeper queue.
    ./seed-clinical-flow.ps1 -DoctorCount 100 -ReceptionistCount 100 -QueueVolume 3000
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:8000",
    [int]$DoctorCount = 10,
    [int]$ReceptionistCount = 10,
    [int]$QueueVolume = 300,
    [string]$UserPassword = "LoadTest#Pass1"
)

$ErrorActionPreference = "Stop"

$seedPassword = $env:AUTH_SEED_PASSWORD
if ([string]::IsNullOrWhiteSpace($seedPassword)) {
    throw "AUTH_SEED_PASSWORD is not set. Set it to the value in the repo-root .env " +
          "so this script can log in as the development-seeded accounts."
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

Write-Host "Authenticating as development-seeded accounts..." -ForegroundColor Cyan
$adminToken = Get-Token "admin.fernando" $seedPassword
$receptionToken = Get-Token "reception.silva" $seedPassword

# --- Doctor accounts, one room each ---------------------------------------------
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

# --- Receptionist accounts ------------------------------------------------------
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

# --- Waiting queue: one check-in per doctor cycle -------------------------------
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

    if ($i % 100 -eq 0) { Write-Host "  $i / $QueueVolume" -ForegroundColor DarkGray }
}

Write-Host "  checked in $checkedIn / $QueueVolume patients" -ForegroundColor Green
Write-Host "`nClinical-flow seed complete. Give QueueService a few seconds to drain the" -ForegroundColor Green
Write-Host "patient-checked-in topic before starting a run." -ForegroundColor Green
