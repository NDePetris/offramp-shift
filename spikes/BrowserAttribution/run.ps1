param([ValidateSet('build','test','monitor','snapshot')][string]$Command = 'test')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$local = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $local) { $local } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (Test-Path -LiteralPath $local) { $env:DOTNET_ROOT = Split-Path $local -Parent }
$project = Join-Path $PSScriptRoot 'Probe/Probe.csproj'
if ($Command -in @('build','test')) {
    & $dotnet build $project -c Release --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Command -eq 'test') {
        & (Join-Path $PSScriptRoot 'Tests/registration.test.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet restore (Join-Path $PSScriptRoot 'Tests/Tests.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet run --project (Join-Path $PSScriptRoot 'Tests/Tests.csproj') -c Release --no-launch-profile --no-restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & node --test (Join-Path $PSScriptRoot 'Tests/extension.test.mjs')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & node --check (Join-Path $PSScriptRoot 'extension/worker.js')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & node --check (Join-Path $PSScriptRoot 'extension/bind.js')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & node (Join-Path $PSScriptRoot 'Tests/transport.test.mjs')
    }
} else {
    & $dotnet (Join-Path $PSScriptRoot 'Probe/bin/Release/net10.0/BrowserProbe.dll') $Command
}
exit $LASTEXITCODE
