param(
    [ValidateSet('build', 'test', 'format', 'snapshot', 'watch', 'foreground-test', 'user-test', 'monitor', 'delivery-test', 'isolation-test', 'help')]
    [string]$Command = 'help',
    [Parameter(ValueFromRemainingArguments = $true)] [string[]]$ProbeArguments
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$localDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path $localDotnet -Parent }
$project = Join-Path $PSScriptRoot 'Probe/Probe.csproj'
$tests = Join-Path $PSScriptRoot 'Tests/Tests.csproj'
if ($Command -in @('build', 'test', 'format')) {
    & $dotnet restore $tests --configfile (Join-Path $taskRoot 'src/NuGet.Config') --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build $tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Command -eq 'test') {
        & $dotnet run --project $tests -c Release --no-build --no-restore --no-launch-profile
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if ($Command -eq 'format') {
        foreach ($item in @($project, $tests,
            (Join-Path $taskRoot 'src/OfframpShift.WindowsObservation/OfframpShift.WindowsObservation.csproj'),
            (Join-Path $taskRoot 'src/OfframpShift.ActivityMonitoring/OfframpShift.ActivityMonitoring.csproj'))) {
            & $dotnet format $item --verify-no-changes --no-restore
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
} else {
    $dll = Join-Path $PSScriptRoot 'Probe/bin/Release/net10.0/ObservationProbe.dll'
    if (!(Test-Path -LiteralPath $dll)) { throw 'Build first: ./spikes/WindowsObservation/run.ps1 build' }
    & $dotnet $dll $Command @ProbeArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
