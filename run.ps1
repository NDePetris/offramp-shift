param([ValidateSet('build', 'test', 'format')][string]$Command = 'test')
$ErrorActionPreference = 'Stop'
$localDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path $localDotnet -Parent }
$projects = @(
    'tests/OfframpShift.Core.Tests/OfframpShift.Core.Tests.csproj',
    'tests/OfframpShift.Persistence.Tests/OfframpShift.Persistence.Tests.csproj'
)
foreach ($relativeProject in $projects) {
    $project = Join-Path $PSScriptRoot $relativeProject
    & $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'src/NuGet.Config') --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Command -eq 'test') {
        & $dotnet run --project $project -c Release --no-build --no-restore --no-launch-profile
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
if ($Command -eq 'format') {
    foreach ($relativeProject in @(
        'src/OfframpShift.Core/OfframpShift.Core.csproj',
        'src/OfframpShift.Persistence/OfframpShift.Persistence.csproj'
    ) + $projects) {
        & $dotnet format (Join-Path $PSScriptRoot $relativeProject) --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
exit 0
