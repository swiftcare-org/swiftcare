<#
.SYNOPSIS
    Seeds isolated local data for SWC-136 Sprint 4 performance tests.

.DESCRIPTION
    Reuses the SWC-126 clinical seed, then creates dedicated Admin accounts and
    mutable target accounts. It writes git-ignored CSV files consumed by the
    Sprint 4 Activity Feed, history, reports and Admin thread groups. All data is
    created through the API Gateway; the script never writes directly to MySQL.
#>
[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:8016",
    [int]$DoctorCount = 10,
    [int]$ReceptionistCount = 10,
    [int]$AdminCount = 4,
    [int]$QueueVolume = 300,
    [string]$UserPassword = "LoadTest#Pass1",
    [int]$QueueWaitSeconds = 120
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:AUTH_SEED_PASSWORD)) {
    throw "AUTH_SEED_PASSWORD must be set for the isolated SWC-136 environment."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = Join-Path $scriptDir "data"

function Get-Token([string]$username, [string]$password) {
    $response = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/auth/login" `
        -ContentType "application/json" `
        -Body (@{ username = $username; password = $password } | ConvertTo-Json -Compress)
    return [string]$response.token
}

function Get-OrCreateUser(
    [string]$adminToken,
    [hashtable]$account
) {
    $headers = @{ Authorization = "Bearer $adminToken" }
    try {
        return Invoke-RestMethod -Method Post -Uri "$GatewayUrl/api/users" `
            -Headers $headers -ContentType "application/json" `
            -Body ($account | ConvertTo-Json -Compress)
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -ne 409 -and $status -ne 400) { throw }

        $existing = @(Invoke-RestMethod -Method Get -Uri "$GatewayUrl/api/users" -Headers $headers) |
            Where-Object { $_.username -eq $account.username } |
            Select-Object -First 1
        if ($null -eq $existing) { throw }
        return $existing
    }
}

Write-Host "Seeding the reusable SWC-126 clinical accounts and queue..." -ForegroundColor Cyan
& (Join-Path $scriptDir "seed-clinical-flow.ps1") `
    -GatewayUrl $GatewayUrl `
    -DoctorCount $DoctorCount `
    -ReceptionistCount $ReceptionistCount `
    -QueueVolume $QueueVolume `
    -UserPassword $UserPassword

$adminToken = Get-Token "admin.fernando" $env:AUTH_SEED_PASSWORD

Write-Host "Creating $AdminCount isolated Admin/target pairs..." -ForegroundColor Cyan
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
        role = "Admin"
    } | Out-Null

    $target = Get-OrCreateUser $adminToken @{
        username = $targetUsername
        password = $UserPassword
        fullName = $targetName
        role = "Receptionist"
    }

    $adminRows.Add("$adminUsername,$UserPassword,$($target.userId),$targetName")
}

Set-Content -Path (Join-Path $dataDir "sprint4-admins.csv") -Value $adminRows -Encoding utf8

$doctorRows = @(Import-Csv -LiteralPath (Join-Path $dataDir "clinic-doctors.csv"))
$patientRows = @(Import-Csv -LiteralPath (Join-Path $dataDir "clinic-patients.csv"))
if ($doctorRows.Count -eq 0 -or $patientRows.Count -eq 0) {
    throw "The clinical seed did not create the Doctor or patient data required by Sprint 4."
}

$historyRows = [System.Collections.Generic.List[string]]::new()
$historyRows.Add("username,password,patientId")
for ($i = 0; $i -lt $patientRows.Count; $i++) {
    $doctor = $doctorRows[$i % $doctorRows.Count]
    $historyRows.Add("$($doctor.username),$($doctor.password),$($patientRows[$i].patientId)")
}
Set-Content -Path (Join-Path $dataDir "sprint4-history.csv") -Value $historyRows -Encoding utf8

$doctorToken = Get-Token $doctorRows[0].username $doctorRows[0].password
$deadline = (Get-Date).AddSeconds($QueueWaitSeconds)
do {
    # Invoke-RestMethod preserves a top-level JSON array as one pipeline object
    # when invoked inside @(...), so that wrapper misleadingly reports Count=1.
    # Direct assignment keeps the returned array's actual Count.
    $waiting = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/api/queue/today/waiting" `
        -Headers @{ Authorization = "Bearer $doctorToken" }
    if ($waiting.Count -ge $QueueVolume) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)

if ($waiting.Count -lt $QueueVolume) {
    throw "QueueService exposed only $($waiting.Count) of $QueueVolume seeded waiting patients."
}

$clinicDate = Get-Date -Format "yyyy-MM-dd"
$clinicMonth = Get-Date -Format "yyyy-MM"
Set-Content -Path (Join-Path $dataDir "sprint4-run.properties") -Encoding utf8 -Value @(
    "reportDate=$clinicDate"
    "reportMonth=$clinicMonth"
)

Write-Host "Sprint 4 seed complete:" -ForegroundColor Green
Write-Host "  doctors=$DoctorCount receptionists=$ReceptionistCount admins=$AdminCount waiting=$($waiting.Count)"
Write-Host "  history rows=$($historyRows.Count - 1) date=$clinicDate month=$clinicMonth"
