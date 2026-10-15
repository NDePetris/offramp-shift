param(
    [ValidateSet('test', 'dry-run', 'once', 'repeat', 'manual')]
    [string]$Command = 'dry-run',
    [switch]$ConfirmExperimentalReadOnly,
    [switch]$OutsideToolSandbox,
    [string]$MonitorLayoutAndDpi
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$localDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path $localDotnet -Parent }
$live = $Command -in @('once', 'repeat', 'manual')
if ($live -and (!$ConfirmExperimentalReadOnly -or !$OutsideToolSandbox -or [string]::IsNullOrWhiteSpace($MonitorLayoutAndDpi))) {
    throw 'Live reads require -ConfirmExperimentalReadOnly -OutsideToolSandbox -MonitorLayoutAndDpi <anonymous layout/scaling description>. Run test and dry-run first.'
}
$project = Join-Path $PSScriptRoot 'Compatibility/Compatibility.csproj'
& $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Hash all compiled source inputs, including linked source and Core, for dirty-tree provenance.
$inputs = @(
    Get-ChildItem (Join-Path $PSScriptRoot 'Compatibility') -File -Filter '*.cs'
    Get-Item $project
    Get-Item (Join-Path $PSScriptRoot 'Directory.Build.props')
    Get-Item (Join-Path $PSScriptRoot 'Probe/UserObservationGate.cs')
    Get-Item (Join-Path $taskRoot 'global.json')
    Get-ChildItem (Join-Path $taskRoot 'src/OfframpShift.Core') -File
    Get-Item (Join-Path $taskRoot 'src/Directory.Build.props')
    foreach ($name in @('DesktopWorker.cs', 'Evidence.cs', 'Native.cs')) {
        Get-Item (Join-Path $taskRoot "src/OfframpShift.WindowsObservation/$name")
    }
) | Sort-Object FullName
$hashText = (($inputs | Get-FileHash -Algorithm SHA256).Hash -join '')
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $sourceDigest = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashText))).Replace('-', '') }
finally { $sha.Dispose() }
$revision = & git -C $taskRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine tested revision' }
$dirty = [bool](& git -C $taskRoot status --porcelain)
$sdk = & $dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine SDK' }
[ordered]@{ stage = 'Provenance'; revision = $revision; dirtyWorkingTree = $dirty; compiledSourceSha256 = $sourceDigest;
    sdk = $sdk; outsideToolSandboxOperatorAttested = [bool]$OutsideToolSandbox;
    monitorLayoutAndDpiOperatorReported = $MonitorLayoutAndDpi } | ConvertTo-Json -Compress
$dll = Join-Path $PSScriptRoot 'Compatibility/bin/Release/net10.0/DesktopCompatibility.dll'
if ($Command -eq 'test') {
    $guardTests = Join-Path $PSScriptRoot 'CompatibilityGuardTests/CompatibilityGuardTests.csproj'
    & $dotnet restore $guardTests --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build $guardTests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet (Join-Path $PSScriptRoot 'CompatibilityGuardTests/bin/Release/net10.0/CompatibilityGuardTests.dll')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($live) {
    & $dotnet $dll $Command '--confirm-experimental-read-only-26200.9711' '--outside-tool-sandbox'
} else {
    & $dotnet $dll $Command
}
exit $LASTEXITCODE
