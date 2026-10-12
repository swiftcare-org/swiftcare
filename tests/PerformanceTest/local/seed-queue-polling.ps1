<#
.SYNOPSIS
    One-time data seeding for the SWC-87 queue-polling JMeter suite.

.DESCRIPTION
    Seeds what SWC-20-today-queue.jmx / SWC-21-waiting-pool.jmx / SWC-23-display.jmx
    need beyond the existing Sprint 1 seed: a Doctor account pool (for the
    Receptionist-only today-queue and Doctor-only waiting-pool endpoints do each
    need their own role) and enough today-dated queue volume that the three
    polling endpoints return realistic result sets rather than a handful of rows.

    Queue entries are not created directly - QueueService only ever gets them from
    the patient-checked-in Kafka event PatientService publishes on registration
    (see services/QueueService/Services/PatientCheckedInConsumer.cs), so this script
    registers patients through the gateway the same way seed.ps1 does and lets the
    existing consumer turn them into today-dated Waiting queue entries.

    Writes data/doctors.csv and data/receptionists.csv (username,password), read by
    the two authenticated samplers via CSV Data Set Config, login-once-per-thread.

.PARAMETER GatewayUrl
    API Gateway base URL. Default: http://localhost:8000

.PARAMETER DoctorCount
    Number of load-test Doctor accounts to create. Default: 10

.PARAMETER ReceptionistCount
    Number of load-test Receptionist accounts to create. Default: 10

.PARAMETER QueueVolume
    Number of additional patients to check in, each producing one today-dated
    Waiting queue entry. Default: 150

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.EXAMPLE
    # Set AUTH_SEED_PASSWORD to the value in the repo-root .env, then run:
    ./seed-queue-polling.ps1
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:8000",
    [int]$DoctorCount = 10,
    [int]$ReceptionistCount = 10,
    [int]$QueueVolume = 150,
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

Write-Host "Authenticating as development-seeded accounts..." -ForegroundColor Cyan
$adminToken = Get-Token "admin.fernando" $seedPassword
$receptionToken = Get-Token "reception.silva" $seedPassword

# --- Doctor accounts (SWC-21 waiting-pool authz) --------------------------------
Write-Host "Creating $DoctorCount load-test Doctor accounts..." -ForegroundColor Cyan
$doctorRows = [System.Collections.Generic.List[string]]::new()
$doctorRows.Add("username,password")

for ($i = 1; $i -le $DoctorCount; $i++) {
    $username = "perf.doctor.{0:D3}" -f $i
    $body = @{
        username   = $username
        password   = $UserPassword
        fullName   = "Perf Doctor $i"
        role       = "Doctor"
        roomNumber = "PR-{0:D2}" -f $i
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
    $doctorRows.Add("$username,$UserPassword")
}

Set-Content -Path (Join-Path $dataDir "doctors.csv") -Value $doctorRows -Encoding utf8
Write-Host "  wrote data/doctors.csv ($($doctorRows.Count - 1) rows)" -ForegroundColor Green

# --- Receptionist accounts (SWC-20 today-queue authz) ---------------------------
Write-Host "Creating $ReceptionistCount load-test Receptionist accounts..." -ForegroundColor Cyan
$receptionistRows = [System.Collections.Generic.List[string]]::new()
$receptionistRows.Add("username,password")

for ($i = 1; $i -le $ReceptionistCount; $i++) {
    $username = "perf.reception.{0:D3}" -f $i
    $body = @{
        username = $username
        password = $UserPassword
        fullName = "Perf Reception $i"
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
    $receptionistRows.Add("$username,$UserPassword")
}

Set-Content -Path (Join-Path $dataDir "receptionists.csv") -Value $receptionistRows -Encoding utf8
Write-Host "  wrote data/receptionists.csv ($($receptionistRows.Count - 1) rows)" -ForegroundColor Green

# --- Queue volume: check in patients so QueueService gets today-dated entries ---
Write-Host "Checking in $QueueVolume patients to seed today's queue..." -ForegroundColor Cyan
$stamp = Get-Date -Format "MMddHHmm"
$checkedIn = 0

for ($i = 1; $i -le $QueueVolume; $i++) {
    $nic = "$stamp{0:D4}" -f $i
    $phone = "07{0:D8}" -f ($i % 100000000)
    $body = @{
        nic         = $nic
        fullName    = "Queue Perf Patient $stamp-$i"
        dateOfBirth = "2000-01-01"
        gender      = "Male"
        address     = "1 Perf Street, Colombo"
        phoneNumber = $phone
        bloodGroup  = "O+"
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
Write-Host "`nQueue-polling seed complete. Give QueueService a few seconds to drain the" -ForegroundColor Green
Write-Host "patient-checked-in topic before running a profile that expects the full volume." -ForegroundColor Green
