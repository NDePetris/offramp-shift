param([switch]$SwitchRoundTrip)
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$runner = Join-Path $PSScriptRoot 'run.ps1'
function Invoke-Probe([string[]]$Arguments) {
    $output = & $runner @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $output" }
    $output -join "`n"
}
$snapshots = @()
for ($i = 0; $i -lt 10; $i++) {
    $snapshots += (Invoke-Probe @('snapshot') | ConvertFrom-Json)
}
$snapshots | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $artifacts 'snapshots.json')
$first = $snapshots[0].snapshot
if (@($snapshots | Where-Object { $_.snapshot.current -ne $first.current }).Count -ne 0) {
    throw 'Active desktop changed during capture; repeat from a stable source.'
}
Write-Output 'Ten read-only snapshots captured through independently started workers.'
if (!$SwitchRoundTrip) { exit 0 }
# This experiment may only bootstrap from the exact existing named roles. No desktops are created or renamed.
$personal = @($first.desktops | Where-Object name -CEQ 'Personal')
$work = @($first.desktops | Where-Object name -CEQ 'Work')
if ($personal.Count -ne 1 -or $work.Count -ne 1 -or $first.current -ne $personal[0].id) {
    throw 'Round trip requires unique existing Personal/Work names and starting on Personal. Use manual GUID binding otherwise.'
}
$bindingPath = Join-Path $artifacts 'roundtrip-bindings.json'
$binding = [ordered]@{
    version = 1; provider = $first.provider; build = $first.configuration.build
    personal = $personal[0].id; work = $work[0].id
}
$binding | ConvertTo-Json | Set-Content -LiteralPath $bindingPath
Invoke-Probe @('bind', 'Personal', $bindingPath) | Set-Content -LiteralPath (Join-Path $artifacts 'bound-Personal.json')
Invoke-Probe @('verify', $bindingPath) | Set-Content -LiteralPath (Join-Path $artifacts 'before.json')
$results = @()
foreach ($role in @('Work', 'Personal')) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $runner switch $role $bindingPath --confirm-experimental
    $code = $LASTEXITCODE
    $clock.Stop()
    $text = $output -join "`n"
    $reply = $null
    try { $reply = $text | ConvertFrom-Json } catch { }
    $results += [ordered]@{ role = $role; exitCode = $code; totalElapsedMs = $clock.ElapsedMilliseconds; reply = $reply; raw = $text }
    $results | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $artifacts 'roundtrip.json')
    if ($code -ne 0 -or !$reply.ok -or $reply.switch.status -ne 'Verified') {
        throw 'Switch was rejected or indeterminate. STOP; no automatic retry/return. Check Task View manually.'
    }
    Invoke-Probe @('bind', $role, $bindingPath) | Set-Content -LiteralPath (Join-Path $artifacts ("bound-$role.json"))
    # Re-read persisted identities in a new parent/worker before the next explicit leg.
    Invoke-Probe @('verify', $bindingPath) | Set-Content -LiteralPath (Join-Path $artifacts ("verified-$role.json"))
}
Write-Output 'Two individually verified GUID switches completed. This is not the 200-transition certification.'
