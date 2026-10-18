param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Squirrel requires a semantic version with exactly three numeric components.' }
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
New-Item -ItemType Directory -Force out/logs,out/receipts | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$binding = Get-SourceBinding
$source = $binding.source
$started = [DateTime]::UtcNow.ToString('o')
& $dotnet publish src/MaterialDockerImage/MaterialDockerImage.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true "-p:Version=$Version" "-p:BuildTimestamp=$started" "-p:InformationalVersion=$Version+$source" -o out/publish 2>&1 | Tee-Object out/logs/publish.log
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) { exit $exitCode }
if (-not (Test-Path out/publish/MaterialDockerImage.exe)) { throw 'Published executable missing.' }
& .tools/squirrel/tools/rcedit.exe out/publish/MaterialDockerImage.exe --set-version-string SquirrelAwareVersion 1
if ($LASTEXITCODE -ne 0) { throw 'Native apphost Squirrel lifecycle marker could not be applied.' }
Assert-SourceBinding $binding
$files = Get-ChildItem out/publish -File -Recurse | ForEach-Object { @{path=$_.FullName.Substring((Resolve-Path out/publish).Path.Length+1); sha256=(Get-Sha256 $_.FullName); bytes=$_.Length} }
@{source=$source; sourceBinding=$binding; version=$Version; startedUtc=$started; completedUtc=[DateTime]::UtcNow.ToString('o'); sdk=(& $dotnet --version); runtime='win-x64'; selfContained=$true; files=@($files)} | ConvertTo-Json -Depth 7 | Set-Content out/receipts/build.json

