param([ValidateSet('build', 'test', 'format')][string]$Command = 'test')
$ErrorActionPreference = 'Stop'
$localDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path $localDotnet -Parent }
$project = Join-Path $PSScriptRoot 'tests/OfframpShift.Core.Tests/OfframpShift.Core.Tests.csproj'
& $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'src/NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Command -eq 'test') {
    & $dotnet run --project $project -c Release --no-build --no-restore --no-launch-profile
} elseif ($Command -eq 'format') {
    & $dotnet format (Join-Path $PSScriptRoot 'src/OfframpShift.Core/OfframpShift.Core.csproj') --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet format $project --verify-no-changes --no-restore
}
exit $LASTEXITCODE
