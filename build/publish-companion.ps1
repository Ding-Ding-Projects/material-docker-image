param([string]$Version = '0.1.0', [switch]$NoReleaseCopy)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'The build companion requires a semantic version with exactly three numeric components.' }
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
New-Item -ItemType Directory -Force out/logs,out/receipts,out/companion | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$binding = Get-SourceBinding
$source = $binding.source
$stage = Join-Path $root 'out/companion/publish'
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$started = [DateTime]::UtcNow.ToString('o')
# Native stderr must not become a terminating error under 'Stop'; the exit code decides.
$ErrorActionPreference = 'Continue'
& $dotnet publish src/BuildCompanion/BuildCompanion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true "-p:Version=$Version" "-p:InformationalVersion=$Version+$source" -o $stage 2>&1 | Tee-Object out/logs/publish-companion.log
$exitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($exitCode -ne 0) { exit $exitCode }
$built = Join-Path $stage 'MaterialDockerImageBuild.exe'
if (-not (Test-Path $built)) { throw 'Published companion executable missing.' }
$extra = @(Get-ChildItem $stage -File | Where-Object { $_.Name -ne 'MaterialDockerImageBuild.exe' })
if ($extra.Count -gt 0) { throw "Companion publish is not a single file; unexpected output: $($extra.Name -join ', ')" }
$target = Join-Path $root 'out/companion/MaterialDockerImageBuild.exe'
Copy-Item -LiteralPath $built -Destination $target -Force
Remove-Item -LiteralPath $stage -Recurse -Force
Assert-SourceBinding $binding
$item = Get-Item $target
@{source=$source; sourceBinding=$binding; version=$Version; startedUtc=$started; completedUtc=[DateTime]::UtcNow.ToString('o'); sdk=(& $dotnet --version); runtime='win-x64'; selfContained=$true; singleFile=$true; file=@{name=$item.Name; sha256=(Get-Sha256 $target); bytes=$item.Length}} | ConvertTo-Json -Depth 7 | Set-Content out/receipts/companion.json
if (-not $NoReleaseCopy -and (Test-Path out/releases)) { Copy-Item $target, out/receipts/companion.json out/releases -Force }
Write-Host "Companion ready: out/companion/MaterialDockerImageBuild.exe ($($item.Length) bytes)."
