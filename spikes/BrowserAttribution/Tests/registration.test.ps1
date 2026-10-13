# Exercise the real manifest expression without installing a host or touching HKCU.
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$registration = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot '../register-host.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Registration script does not parse.' }
$manifestExpressions = @($registration.FindAll({
    param($ast)
    $ast -is [System.Management.Automation.Language.HashtableAst] -and
        @($ast.KeyValuePairs | Where-Object { $_.Item1.Extent.Text -eq 'allowed_origins' }).Count -eq 1
}, $true))
if ($manifestExpressions.Count -ne 1) { throw 'Expected exactly one native host manifest expression.' }
# Evaluate only the hashtable, never the surrounding registration/file-write code.
$manifestExpression = [scriptblock]::Create($manifestExpressions[0].Extent.Text)
$name = 'com.offramp_shift.pr0b'
$launcher = 'C:\probe fixture\host.cmd'
$ChromeId = 'a' * 32
foreach ($EdgeId in @($ChromeId, ('b' * 32))) {
    $expectedCount = if ($EdgeId -eq $ChromeId) { 1 } else { 2 }
    $manifest = (& $manifestExpression | ConvertTo-Json -Depth 4) | ConvertFrom-Json
    if ($manifest.allowed_origins -isnot [array]) { throw 'allowed_origins must serialize as a JSON array, including one origin.' }
    if ($manifest.allowed_origins.Count -ne $expectedCount) { throw 'Incorrect deduplicated allowlist size.' }
    foreach ($id in @($ChromeId, $EdgeId)) {
        if ($manifest.allowed_origins -cnotcontains "chrome-extension://$id/") { throw 'Missing exact extension origin.' }
    }
    if ($manifest.name -cne $name -or $manifest.type -cne 'stdio' -or $manifest.path -cne $launcher) {
        throw 'Host name, transport or launcher changed during serialization.'
    }
    if ($manifest.allowed_origins | Where-Object { $_ -cnotmatch '^chrome-extension://[a-p]{32}/$' }) {
        throw 'Allowlist contains a non-exact extension origin.'
    }
}
Write-Output 'PASS: native manifest JSON arrays for equal/distinct IDs, deduplication, exact origins and launcher path. No registry or browser interaction.'
exit 0
