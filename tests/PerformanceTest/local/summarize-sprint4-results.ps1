<# Calculates the pre-registered SWC-136 thresholds from a JMeter CSV JTL. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$JtlPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [int]$WarmupSeconds = 0
)

$ErrorActionPreference = "Stop"

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

$all = @(Import-Csv -LiteralPath $JtlPath)
if ($all.Count -eq 0) { throw "The JTL contains no samples: $JtlPath" }
$firstTimestamp = ($all | Measure-Object -Property timeStamp -Minimum).Minimum
$windowStart = [int64]$firstTimestamp + ($WarmupSeconds * 1000L)
$samples = @($all | Where-Object {
    [int64]$_.timeStamp -ge $windowStart -and ($_.label.StartsWith('[READ]') -or $_.label.StartsWith('[WRITE]'))
})
if ($samples.Count -eq 0) { throw "No measured READ/WRITE samples remain after warm-up." }

$result = [ordered]@{
    Jtl = [System.IO.Path]::GetFullPath($JtlPath)
    WarmupSeconds = $WarmupSeconds
    Samples = $samples.Count
    Classes = [ordered]@{}
}

foreach ($class in @('READ','WRITE')) {
    $classSamples = @($samples | Where-Object { $_.label.StartsWith("[$class]") })
    $elapsed = [double[]]@($classSamples | ForEach-Object { [double]$_.elapsed })
    $functionalErrors = @($classSamples | Where-Object {
        $_.responseCode -notmatch '^2\d\d$' -or
        (-not [string]::IsNullOrWhiteSpace($_.failureMessage) -and $_.failureMessage -notmatch 'lasted too long')
    }).Count
    $durationBreaches = @($classSamples | Where-Object failureMessage -Match 'lasted too long').Count

    $ordered = @($classSamples | Sort-Object { [int64]$_.timeStamp })
    $third = [Math]::Max(1, [Math]::Floor($ordered.Count / 3))
    $firstThird = [double[]]@($ordered | Select-Object -First $third | ForEach-Object { [double]$_.elapsed })
    $lastThird = [double[]]@($ordered | Select-Object -Last $third | ForEach-Object { [double]$_.elapsed })
    $firstP95 = Get-Percentile $firstThird 0.95
    $lastP95 = Get-Percentile $lastThird 0.95
    $drift = if ($firstP95 -gt 0) { [Math]::Round((($lastP95 / $firstP95) - 1) * 100, 2) } else { $null }
    $p95 = Get-Percentile $elapsed 0.95
    $p99 = Get-Percentile $elapsed 0.99
    $errorRate = if ($classSamples.Count) { [Math]::Round(100 * $functionalErrors / $classSamples.Count, 4) } else { 0 }
    $p95Limit = if ($class -eq 'READ') { 800 } else { 1500 }
    $p99Limit = if ($class -eq 'READ') { 1500 } else { 3000 }

    $result.Classes[$class] = [ordered]@{
        Samples = $classSamples.Count
        P95Ms = $p95
        P99Ms = $p99
        FunctionalErrors = $functionalErrors
        ErrorRatePercent = $errorRate
        DurationAssertionBreaches = $durationBreaches
        FirstThirdP95Ms = $firstP95
        LastThirdP95Ms = $lastP95
        DriftPercent = $drift
        P95Pass = ($p95 -le $p95Limit)
        P99Pass = ($p99 -le $p99Limit)
        ErrorRatePass = ($errorRate -le 0.5)
        DriftPass = ($null -ne $drift -and $drift -le 20)
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
$result.Classes.GetEnumerator() | ForEach-Object {
    [pscustomobject]@{ Class=$_.Key; Samples=$_.Value.Samples; P95Ms=$_.Value.P95Ms; P99Ms=$_.Value.P99Ms;
        ErrorRate=$_.Value.ErrorRatePercent; Drift=$_.Value.DriftPercent; Pass=($_.Value.P95Pass -and $_.Value.P99Pass -and $_.Value.ErrorRatePass -and $_.Value.DriftPass) }
} | Format-Table -AutoSize
Write-Output "Threshold summary: $resolvedOutput"
