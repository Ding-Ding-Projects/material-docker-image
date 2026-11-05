param([switch]$DockerSmoke)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 turns any native stderr line into a terminating error under 'Stop'
# (for example the first-run notice of a freshly extracted SDK). Native commands are judged by exit code instead.
if ($PSVersionTable.PSVersion.Major -ge 7) { $PSNativeCommandUseErrorActionPreference = $false }
Set-Location (Split-Path $PSScriptRoot -Parent)
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force out/logs | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$arguments = @('run','--project','tests/MaterialDockerImage.Core.Tests.csproj','-c','Release')
if ($DockerSmoke) { $arguments += @('--','--docker-smoke') }
$log = if ($DockerSmoke) { 'out/logs/docker-smoke.log' } else { 'out/logs/core-tests.log' }
& $dotnet @arguments 2>&1 | Tee-Object $log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $DockerSmoke) {
    # The checks link only framework-free sources, so compile the window application itself too: a page that does not
    # build must fail here, not in the release run.
    & $dotnet build src/MaterialDockerImage/MaterialDockerImage.csproj -c Release -nologo -v q 2>&1 | Tee-Object out/logs/app-build.log
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build src/BuildCompanion/BuildCompanion.csproj -c Release -nologo -v q 2>&1 | Tee-Object out/logs/companion-build.log
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet run --project tests/UiState/UiState.csproj -c Release 2>&1 | Tee-Object out/logs/ui-state-tests.log
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $node = (Get-Content .tools/node-path.txt -Raw).Trim()
    & $node scripts/test-feature-checklist.mjs 2>&1 | Tee-Object out/logs/feature-checklist-tests.log
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
