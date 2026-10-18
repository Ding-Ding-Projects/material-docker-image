param([switch]$DockerSmoke)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force out/logs | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$arguments = @('run','--project','tests/MaterialDockerImage.Core.Tests.csproj','-c','Release')
if ($DockerSmoke) { $arguments += @('--','--docker-smoke') }
$log = if ($DockerSmoke) { 'out/logs/docker-smoke.log' } else { 'out/logs/core-tests.log' }
& $dotnet @arguments 2>&1 | Tee-Object $log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $DockerSmoke) {
    & $dotnet run --project tests/UiState/UiState.csproj -c Release 2>&1 | Tee-Object out/logs/ui-state-tests.log
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
