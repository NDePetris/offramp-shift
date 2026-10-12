param(
    [ValidateSet('build', 'test', 'snapshot', 'watch', 'bind', 'verify', 'switch', 'isolation-test', 'safety-test', 'help')]
    [string]$Command = 'help',
    [Parameter(ValueFromRemainingArguments = $true)] [string[]]$ProbeArguments
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$localDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
# Ensure framework-dependent apphosts and child processes find the local runtime.
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path $localDotnet -Parent }
$project = Join-Path $PSScriptRoot 'Probe\Probe.csproj'
if ($Command -in @('build', 'test')) {
    & $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Command -eq 'test') {
        $tests = Join-Path $PSScriptRoot 'Probe.Tests\Probe.Tests.csproj'
        & $dotnet restore $tests --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet run --project $tests -c Release --no-launch-profile --no-restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet (Join-Path $PSScriptRoot 'Probe\bin\Release\net10.0\DesktopProbe.dll') isolation-test
    }
} else {
    $dll = Join-Path $PSScriptRoot 'Probe\bin\Release\net10.0\DesktopProbe.dll'
    if (!(Test-Path -LiteralPath $dll)) { throw "Build first: ./spikes/VirtualDesktop/run.ps1 build" }
    & $dotnet $dll $Command @ProbeArguments
}
exit $LASTEXITCODE
