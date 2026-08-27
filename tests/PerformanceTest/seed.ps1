<#
.SYNOPSIS
    One-time data seeding for the SwiftCare JMeter load test.

.DESCRIPTION
    Creates a pool of staff accounts and a bulk of patient records through the
    API Gateway, then writes the three CSV files that swiftcare-load.jmx reads:
    data/users.csv, data/patients.csv, data/search-terms.csv.

    Run once against a database you can afford to grow (or reset with
    'docker compose down -v' afterwards). Safe to re-run - each run stamps its
    patients with the current MMddHHmm so NICs do not collide between runs;
    user accounts use fixed names and an already-exists response is tolerated.

.PARAMETER GatewayUrl
    API Gateway base URL. Default: http://localhost:8000

.PARAMETER UserCount
    Number of load-test staff accounts to create. Default: 25

.PARAMETER PatientCount
    Number of patient records to register. Default: 500

.PARAMETER UserPassword
    Password assigned to every created load-test account. Default: LoadTest#Pass1

.EXAMPLE
    $env:AUTH_SEED_PASSWORD = "<value from repo-root .env>"
    ./seed.ps1

.EXAMPLE
    ./seed.ps1 -PatientCount 2000 -UserCount 50
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:8000",
    [int]$UserCount = 25,
    [int]$PatientCount = 500,
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

# --- Staff accounts -------------------------------------------------------------
Write-Host "Creating $UserCount load-test staff accounts..." -ForegroundColor Cyan
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

# --- Patient records ----------------------------------------------------------
Write-Host "Registering $PatientCount patients..." -ForegroundColor Cyan
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

# --- Search terms -----------------------------------------------------------
# A spread of terms: this run's patients, broad name fragments, and phone/NIC
# prefixes - so the search endpoint returns result sets of varying size.
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

Write-Host "`nSeeding complete." -ForegroundColor Green
