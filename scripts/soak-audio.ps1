[CmdletBinding()]
param(
    [double]$MinutesPerPhase = 7.5,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\docs\perf')
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\KyappuTone.Soak\KyappuTone.Soak.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $OutputDir "soak-$stamp"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet was not found on PATH.'
    exit 127
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$started = Get-Date
$failures = 0
$summaries = @()

$phases = @(
    @{ Name = 'eq'; Extra = @() },
    @{ Name = 'eq-viz'; Extra = @('--visualizer') }
)

foreach ($phase in $phases) {
    $csv = Join-Path $outDir ("{0}.csv" -f $phase.Name)
    $arguments = @(
        'run', '--project', $project, '-c', 'Release', '--',
        '--minutes', $MinutesPerPhase.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--csv', $csv
    ) + $phase.Extra

    $output = & dotnet @arguments 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -Path (Join-Path $outDir ("{0}.log" -f $phase.Name))
    $summaries += $output

    if ($code -ne 0) {
        $failures++
        Write-Warning ("Phase {0} failed with exit code {1}." -f $phase.Name, $code)
    }
}

$since = $started
$crashes = @(
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since; Level = 1, 2 } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like '*KyappuTone*' }
)

$summaryPath = Join-Path $outDir 'summary.txt'
$lines = @(
    "started=$($started.ToString('o'))",
    "minutes_per_phase=$MinutesPerPhase",
    "failures=$failures",
    "crash_events=$($crashes.Count)"
)
$lines += $summaries
$lines | Set-Content -Path $summaryPath

Write-Host ("Soak artifacts: {0}" -f $outDir)
Write-Host ("Failures: {0}; crash events: {1}" -f $failures, $crashes.Count)

if ($failures -gt 0 -or $crashes.Count -gt 0) {
    exit 1
}

exit 0
