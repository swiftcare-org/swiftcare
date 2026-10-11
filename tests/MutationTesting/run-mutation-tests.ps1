<#
.SYNOPSIS
    Runs Stryker.NET mutation testing for SwiftCare services and writes a combined summary.

.EXAMPLE
    ./run-mutation-tests.ps1
    Full run of all seven services.

.EXAMPLE
    ./run-mutation-tests.ps1 -Service PrescriptionService -Mutate "**/Services/PrescriptionManagementService.cs" -OpenReport
    One file of one service, for a quick demo.

.EXAMPLE
    ./run-mutation-tests.ps1 -Service "QueueService,MedicalRecordService"
    Several services. A comma-separated string also works through powershell -File.

.EXAMPLE
    ./run-mutation-tests.ps1 -Since develop
    Mutates only code changed since develop (pull request mode).
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Service = @('All'),

    [string]$Mutate,

    [string]$Since,

    [int]$Concurrency = 0,

    [switch]$OpenReport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allServices = @('ApiGateway', 'AuthService', 'MedicalRecordService', 'NotificationService', 'PatientService', 'PrescriptionService', 'QueueService')

# powershell -File passes "A, B" as separate raw strings, so split and trim every value.
$requested = @($Service | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$unknown = @($requested | Where-Object { $_ -ne 'All' -and $allServices -notcontains $_ })
if ($unknown) {
    throw ('Unknown service: {0}. Valid values: All, {1}.' -f ($unknown -join ', '), ($allServices -join ', '))
}
if ($requested -contains 'All') { $selected = $allServices } else { $selected = $requested }
if ($Mutate -and @($selected).Count -ne 1) {
    throw '-Mutate can only be used with exactly one -Service.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$configDir = Join-Path $PSScriptRoot 'configs'
$resultsDir = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

function Get-Value {
    param($Dictionary, [string]$Key)
    if ($Dictionary -is [System.Collections.IDictionary] -and ($Dictionary.Keys -contains $Key)) { return $Dictionary[$Key] }
    return $null
}

function Read-MutantRecords {
    param([string]$ReportPath)

    # Windows PowerShell 5.1's ConvertFrom-Json cannot read large reports, so use the .NET serializer there.
    $text = [System.IO.File]::ReadAllText($ReportPath)
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $report = $text | ConvertFrom-Json -AsHashtable
    }
    else {
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $serializer.MaxJsonLength = [int]::MaxValue
        $report = $serializer.DeserializeObject($text)
    }

    foreach ($file in $report['files'].GetEnumerator()) {
        foreach ($mutant in $file.Value['mutants']) {
            $location = Get-Value $mutant 'location'
            [pscustomobject]@{
                File        = $file.Key
                Line        = (Get-Value (Get-Value $location 'start') 'line')
                Mutator     = (Get-Value $mutant 'mutatorName')
                Replacement = (Get-Value $mutant 'replacement')
                Status      = (Get-Value $mutant 'status')
            }
        }
    }
}

function Get-Count {
    param($Records, [string]$Status)
    return @($Records | Where-Object { $_.Status -eq $Status }).Count
}

function Format-Percent {
    param([int]$Numerator, [int]$Denominator)
    if ($Denominator -eq 0) { return 'n/a' }
    return ('{0:N2}%' -f (100.0 * $Numerator / $Denominator))
}

function Get-RelativePath {
    param([string]$Path)
    if ($Path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    }
    return $Path -replace '\\', '/'
}

function Format-Cell {
    param($Value)
    if ($null -eq $Value) { return '' }
    return ([string]$Value -replace '\|', '\|' -replace '\r?\n', ' ').Trim()
}

function Get-ScoreBand {
    param([double]$Score, $Thresholds)
    if ($Score -ge $Thresholds['high']) { return 'High' }
    if ($Score -ge $Thresholds['low']) { return 'Medium' }
    return 'Low'
}

Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
}
finally {
    Pop-Location
}

$summaries = @()
$allRecords = @()
$failed = @()

foreach ($name in $selected) {
    $testDir = Join-Path (Join-Path $repoRoot 'tests') "$name.UnitTests"
    $configPath = Join-Path $configDir ('stryker-{0}.json' -f $name.ToLowerInvariant())
    $outputDir = Join-Path $resultsDir $name
    if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }

    $arguments = @('stryker', '--config-file', $configPath, '--output', $outputDir)
    if ($Mutate) { $arguments += @('--mutate', $Mutate) }
    if ($Since) { $arguments += "--since:$Since" }
    if ($Concurrency -gt 0) { $arguments += @('--concurrency', "$Concurrency") }

    Write-Host ''
    Write-Host "=== $name ===" -ForegroundColor Cyan
    $started = Get-Date
    Push-Location $testDir
    try {
        & dotnet @arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $duration = (Get-Date) - $started

    $report = Get-ChildItem $outputDir -Recurse -Filter 'mutation-report.json' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $report) {
        Write-Warning "$name produced no report (exit code $exitCode)."
        $failed += $name
        continue
    }

    $records = @(Read-MutantRecords -ReportPath $report.FullName)
    $records | ForEach-Object { $_ | Add-Member -NotePropertyName Service -NotePropertyValue $name }
    $allRecords += $records

    $killed = Get-Count $records 'Killed'
    $timeout = Get-Count $records 'Timeout'
    $survived = Get-Count $records 'Survived'
    $noCoverage = Get-Count $records 'NoCoverage'
    $compileErrors = (Get-Count $records 'CompileError') + (Get-Count $records 'RuntimeError')
    $ignored = Get-Count $records 'Ignored'
    $detected = $killed + $timeout
    $valid = $detected + $survived + $noCoverage
    $thresholds = (Get-Content $configPath -Raw | ConvertFrom-Json).'stryker-config'.thresholds
    $thresholdTable = @{ high = $thresholds.high; low = $thresholds.low }

    if ($valid -gt 0) { $scoreValue = 100.0 * $detected / $valid } else { $scoreValue = 0 }
    if ($exitCode -ne 0) {
        $result = 'Below break'
        $failed += $name
    }
    else {
        $result = Get-ScoreBand -Score $scoreValue -Thresholds $thresholdTable
    }

    $summaries += [pscustomobject]@{
        Service      = $name
        Mutants      = @($records).Count
        Killed       = $killed
        Timeout      = $timeout
        Survived     = $survived
        NoCoverage   = $noCoverage
        CompileError = $compileErrors
        Ignored      = $ignored
        Score        = (Format-Percent $detected $valid)
        CoveredScore = (Format-Percent $detected ($detected + $survived))
        Result       = $result
        Minutes      = ('{0:N1}' -f $duration.TotalMinutes)
    }

    $survivors = @($records | Where-Object { $_.Status -in @('Survived', 'NoCoverage') } | Sort-Object File, Line)
    $lines = @(
        "# Surviving mutants - $name",
        '',
        '| File | Line | Mutator | Replacement | Status |',
        '|---|---:|---|---|---|'
    )
    foreach ($s in $survivors) {
        $lines += ('| {0} | {1} | {2} | `{3}` | {4} |' -f (Format-Cell (Get-RelativePath $s.File)), $s.Line, (Format-Cell $s.Mutator), (Format-Cell $s.Replacement), $s.Status)
    }
    Set-Content -Path (Join-Path $outputDir 'survivors.md') -Value $lines -Encoding UTF8
}

if ($Mutate) { $mode = "Single file: $Mutate" }
elseif ($Since) { $mode = "Changed code since $Since" }
else { $mode = 'Full run' }

$summary = @(
    '# Mutation Testing Summary',
    '',
    "Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm') with Stryker.NET. Mode: $mode.",
    '',
    'Mutation score = (killed + timeout) / (killed + timeout + survived + no coverage).',
    'Covered score leaves out mutants that no test reaches.',
    '',
    '| Service | Mutants | Killed | Timeout | Survived | No coverage | Compile errors | Ignored | Mutation score | Covered score | Result | Minutes |',
    '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|'
)
foreach ($s in $summaries) {
    $summary += ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} |' -f $s.Service, $s.Mutants, $s.Killed, $s.Timeout, $s.Survived, $s.NoCoverage, $s.CompileError, $s.Ignored, $s.Score, $s.CoveredScore, $s.Result, $s.Minutes)
}

if (@($summaries).Count -gt 1) {
    $totalDetected = @($allRecords | Where-Object { $_.Status -in @('Killed', 'Timeout') }).Count
    $totalSurvived = (Get-Count $allRecords 'Survived')
    $totalNoCoverage = (Get-Count $allRecords 'NoCoverage')
    $summary += ('| **Total** | {0} | {1} | {2} | {3} | {4} | {5} | {6} | **{7}** | {8} | | |' -f @($allRecords).Count, (Get-Count $allRecords 'Killed'), (Get-Count $allRecords 'Timeout'), $totalSurvived, $totalNoCoverage, ((Get-Count $allRecords 'CompileError') + (Get-Count $allRecords 'RuntimeError')), (Get-Count $allRecords 'Ignored'), (Format-Percent $totalDetected ($totalDetected + $totalSurvived + $totalNoCoverage)), (Format-Percent $totalDetected ($totalDetected + $totalSurvived)))
}

$byMutator = $allRecords | Where-Object { $_.Status -in @('Survived', 'NoCoverage') } | Group-Object Mutator | Sort-Object Count -Descending
if ($byMutator) {
    $summary += @('', '## Undetected mutants by mutator', '', '| Mutator | Survived | No coverage |', '|---|---:|---:|')
    foreach ($group in $byMutator) {
        $summary += ('| {0} | {1} | {2} |' -f $group.Name, (Get-Count $group.Group 'Survived'), (Get-Count $group.Group 'NoCoverage'))
    }
}

$summary += @('', 'Per-service HTML reports: `results/<Service>/reports/mutation-report.html`. Survivor lists: `results/<Service>/survivors.md`.')
Set-Content -Path (Join-Path $resultsDir 'summary.md') -Value $summary -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary -Encoding UTF8 }

Write-Host ''
$summaries | Format-Table Service, Mutants, Killed, Survived, NoCoverage, Score, CoveredScore, Result, Minutes -AutoSize | Out-Host
Write-Host "Summary written to $(Join-Path $resultsDir 'summary.md')"

if ($OpenReport -and -not $env:CI) {
    foreach ($name in $selected) {
        $html = Get-ChildItem (Join-Path $resultsDir $name) -Recurse -Filter 'mutation-report.html' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($html) { Invoke-Item $html.FullName }
    }
}

if ($failed) {
    Write-Warning ('Below the break threshold or failed: {0}' -f ($failed -join ', '))
    exit 1
}
