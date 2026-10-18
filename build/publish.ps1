param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric components.' }
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
New-Item -ItemType Directory -Force out/logs,out/receipts | Out-Null
$dotnet = (Get-Content .tools/dotnet-path.txt -Raw).Trim()
$source = (& git rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $source = 'uncommitted-initial-tree' }
$before = & git diff HEAD -- src global.json 2>$null | Out-String
$started = [DateTime]::UtcNow.ToString('o')
& $dotnet publish src/MaterialDockerImage/MaterialDockerImage.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" "-p:InformationalVersion=$Version+$source" -o out/publish 2>&1 | Tee-Object out/logs/publish.log
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) { exit $exitCode }
if (-not (Test-Path out/publish/MaterialDockerImage.exe)) { throw 'Published executable missing.' }
$after = & git diff HEAD -- src global.json 2>$null | Out-String
if ($before -ne $after) { throw 'Source changed during publication; discard this candidate and rebuild.' }
$files = Get-ChildItem out/publish -File -Recurse | ForEach-Object { @{path=$_.FullName.Substring((Resolve-Path out/publish).Path.Length+1); sha256=(Get-Sha256 $_.FullName); bytes=$_.Length} }
@{source=$source; version=$Version; startedUtc=$started; completedUtc=[DateTime]::UtcNow.ToString('o'); sdk=(& $dotnet --version); runtime='win-x64'; selfContained=$true; files=@($files)} | ConvertTo-Json -Depth 5 | Set-Content out/receipts/build.json

