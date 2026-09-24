# Phase timing for build-velopack.ps1: dot-sourced from there (". $PSScriptRoot\build-velopack-phase-timing.ps1"),
# split out (#747) so build-velopack.ps1 stays under the project's line-count guideline. Dot-sourcing
# runs this in the caller's own scope, so the $script: variables below still bind to build-velopack.ps1
# itself, exactly as if this were inline.

$script:PhaseTimings = [ordered]@{}
$script:PhaseStopwatch = $null
$script:PhaseName = $null

function Start-BuildPhase {
  param([Parameter(Mandatory)][string]$Name)
  Stop-BuildPhase
  $script:PhaseName = $Name
  $script:PhaseStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
  Write-Host "--- $Name ---"
}

function Stop-BuildPhase {
  if ($null -eq $script:PhaseStopwatch) { return }
  $script:PhaseStopwatch.Stop()
  $seconds = [math]::Round($script:PhaseStopwatch.Elapsed.TotalSeconds, 1)
  $script:PhaseTimings[$script:PhaseName] = $seconds
  Write-Host ("--- {0}: {1}s ---" -f $script:PhaseName, $seconds)
  $script:PhaseStopwatch = $null
  $script:PhaseName = $null
}

function Write-BuildPhaseSummary {
  Stop-BuildPhase
  if ($script:PhaseTimings.Count -eq 0) { return }
  $total = ($script:PhaseTimings.Values | Measure-Object -Sum).Sum
  Write-Host ""
  Write-Host "=== Windows .NET package build timings ==="
  foreach ($entry in $script:PhaseTimings.GetEnumerator()) {
    $share = if ($total -gt 0) { [math]::Round(100 * $entry.Value / $total) } else { 0 }
    Write-Host ("  {0,-38} {1,7}s  {2,3}%" -f $entry.Key, $entry.Value, $share)
  }
  Write-Host ("  {0,-38} {1,7}s" -f "TOTAL", [math]::Round($total, 1))
  if ($env:GITHUB_ACTIONS -eq "true") {
    $parts = $script:PhaseTimings.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)s" }
    Write-Host ("::notice title=Windows .NET package build::" + ($parts -join ", "))
  }
}
