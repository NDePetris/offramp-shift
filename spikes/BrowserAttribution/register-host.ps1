param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$ChromeId,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$EdgeId,
    [switch]$Unregister
)
$ErrorActionPreference = 'Stop'
$name = 'com.offramp_shift.pr0b'
$artifactRoot = Join-Path $PSScriptRoot 'artifacts'
$manifestPath = Join-Path $artifactRoot 'native-host.json'
$keys = @("Software\Google\Chrome\NativeMessagingHosts\$name", "Software\Microsoft\Edge\NativeMessagingHosts\$name")
# Only the exact user keys owned by this spike; do not overwrite existing registrations.
foreach ($subkey in $keys) {
    $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subkey)
    if ($existing) {
        try { if ($existing.GetValue('') -ne $manifestPath) { throw "Foreign registration at HKCU\$subkey; refusing to alter it." } }
        finally { $existing.Dispose() }
    }
}
if ($Unregister) {
    foreach ($subkey in $keys) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($subkey, $false) }
    Write-Output 'Removed only the two PR 0B user registrations. Unload extensions and stop monitor separately.'
    exit 0
}
$exe = Join-Path $PSScriptRoot 'Probe/bin/Release/net10.0/BrowserProbe.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Run ./spikes/BrowserAttribution/run.ps1 build first.' }
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtime = Join-Path $taskRoot '.tools/dotnet'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$launcher = $exe
if (Test-Path -LiteralPath (Join-Path $runtime 'dotnet.exe')) {
    # Local SDK runtime is not globally installed. Browser supports Windows .cmd hosts.
    # Paths must not contain cmd metacharacters; avoid shell interpolation ambiguities.
    if ($runtime -match '[%&|<>^!\r\n"]' -or $exe -match '[%&|<>^!\r\n"]') { throw 'Unsafe path for cmd runtime wrapper.' }
    $launcher = Join-Path $artifactRoot 'host.cmd'
    @('@echo off', "set `"DOTNET_ROOT=$runtime`"", "`"$exe`" %*") | Set-Content -LiteralPath $launcher -Encoding ascii
}
@{ name=$name; description='Offramp Shift PR 0B only'; path=$launcher; type='stdio'; allowed_origins=@("chrome-extension://$ChromeId/", "chrome-extension://$EdgeId/") | Select-Object -Unique } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
foreach ($subkey in $keys) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($subkey)
    try { $key.SetValue('', $manifestPath, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
}
Write-Output 'Registered PR 0B for this user only. Start monitor, reload extensions, and bind each normal window.'
