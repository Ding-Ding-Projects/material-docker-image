param([switch]$DockerSmoke)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force out/logs | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$arguments = @('run','--project','tests/MaterialDockerImage.Core.Tests.csproj','-c','Release')
if ($DockerSmoke) { $arguments += @('--','--docker-smoke') }
& $dotnet @arguments 2>&1 | Tee-Object out/logs/core-tests.log
exit $LASTEXITCODE
