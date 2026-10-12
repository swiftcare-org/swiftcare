<# Captures resources only for one explicitly named Compose project. #>
[CmdletBinding()]
param(
    [string]$ComposeProject = "swiftcare-swc136",
    [Parameter(Mandatory)][string]$OutputPrefix,
    [int]$IntervalSeconds = 10,
    [int]$DurationSeconds = 900
)

$ErrorActionPreference = "Stop"
if ($ComposeProject -ne "swiftcare-swc136") {
    throw "Refusing to monitor '$ComposeProject'. This script is intentionally restricted to swiftcare-swc136."
}
if ([string]::IsNullOrWhiteSpace($env:MYSQL_ROOT_PASSWORD)) {
    throw "MYSQL_ROOT_PASSWORD must be set for the isolated SWC-136 database."
}

$outputPath = [System.IO.Path]::GetFullPath($OutputPrefix)
$outputDir = Split-Path -Parent $outputPath
if (-not (Test-Path -LiteralPath $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

$containerCsv = "$outputPath-containers.csv"
$mysqlCsv = "$outputPath-mysql.csv"
"timestamp,container,cpu_percent,memory_usage,network_io,block_io,pids" | Set-Content $containerCsv
"timestamp,threads_connected,threads_running,max_used_connections" | Set-Content $mysqlCsv

$containers = @(docker ps --filter "label=com.docker.compose.project=$ComposeProject" --format "{{.Names}}")
if ($LASTEXITCODE -ne 0 -or $containers.Count -eq 0) {
    throw "No running containers belong to Compose project '$ComposeProject'."
}
$mysqlContainer = $containers | Where-Object { $_ -match '-mysql-\d+$' } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($mysqlContainer)) { throw "The isolated MySQL container was not found." }

$deadline = (Get-Date).AddSeconds($DurationSeconds)
do {
    $timestamp = Get-Date -Format "o"
    foreach ($line in @(docker stats --no-stream --format "{{json .}}" @containers)) {
        $sample = $line | ConvertFrom-Json
        $values = @(
            $timestamp, $sample.Name, $sample.CPUPerc, $sample.MemUsage,
            $sample.NetIO, $sample.BlockIO, $sample.PIDs
        ) | ForEach-Object { '"' + ([string]$_).Replace('"','""') + '"' }
        ($values -join ',') | Add-Content $containerCsv
    }

    $status = docker exec -e "MYSQL_PWD=$($env:MYSQL_ROOT_PASSWORD)" $mysqlContainer `
        mysql -uroot --batch --skip-column-names -e `
        "SHOW GLOBAL STATUS WHERE Variable_name IN ('Threads_connected','Threads_running','Max_used_connections');"
    $map = @{}
    foreach ($row in $status) {
        $parts = $row -split "`t"
        if ($parts.Count -eq 2) { $map[$parts[0]] = $parts[1] }
    }
    "$timestamp,$($map.Threads_connected),$($map.Threads_running),$($map.Max_used_connections)" | Add-Content $mysqlCsv

    if ((Get-Date) -lt $deadline) { Start-Sleep -Seconds $IntervalSeconds }
} while ((Get-Date) -lt $deadline)

Write-Output "Container resource evidence: $containerCsv"
Write-Output "MySQL resource evidence: $mysqlCsv"
