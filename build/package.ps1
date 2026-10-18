param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$receipt = Get-Content out/receipts/build.json -Raw | ConvertFrom-Json
if ($receipt.version -ne $Version) { throw 'Build version does not match package version.' }
if (-not $receipt.sourceBinding) { throw 'Rebuild required: source binding is absent from the build receipt.' }
Assert-SourceBinding $receipt.sourceBinding
Assert-PayloadBinding $receipt 'out/publish'
New-Item -ItemType Directory -Force out/packages,out/releases | Out-Null
$payload = (Resolve-Path out/publish).Path
$escaped = [Security.SecurityElement]::Escape($payload)
$spec = @"
<?xml version="1.0"?>
<package><metadata><id>MaterialDockerImage</id><version>$Version</version><title>Material Docker Image</title><authors>Ding Ding Projects</authors><description>Native Windows Docker image workspace.</description><copyright>Copyright 2026 Ding Ding Projects</copyright></metadata><files><file src="$escaped\**\*" target="lib\net45" /></files></package>
"@
$spec | Set-Content out/packages/MaterialDockerImage.nuspec -Encoding UTF8
& .tools/nuget.exe pack out/packages/MaterialDockerImage.nuspec -OutputDirectory out/packages -NoPackageAnalysis -NonInteractive 2>&1 | Tee-Object out/logs/nuget-pack.log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$package = (Resolve-Path "out/packages/MaterialDockerImage.$Version.nupkg").Path
& .tools/squirrel/tools/Squirrel.exe --releasify $package --releaseDir (Join-Path $root 'out/releases') --no-msi 2>&1 | Tee-Object out/logs/squirrel.log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-SourceBinding $receipt.sourceBinding
Assert-PayloadBinding $receipt 'out/publish'
foreach ($required in @('Setup.exe','RELEASES',"MaterialDockerImage-$Version-full.nupkg")) {
    if (-not (Test-Path "out/releases/$required")) { throw "Required Squirrel output missing: $required" }
}
$files = Get-ChildItem out/releases -File | ForEach-Object { @{name=$_.Name; sha256=(Get-Sha256 $_.FullName); bytes=$_.Length} }
@{source=$receipt.source; sourceBinding=$receipt.sourceBinding; version=$Version; format='Squirrel.Windows 2.0.1'; unsigned=$true; completedUtc=[DateTime]::UtcNow.ToString('o'); files=@($files)} | ConvertTo-Json -Depth 7 | Set-Content out/receipts/package.json
Copy-Item out/receipts/build.json,out/receipts/package.json out/releases -Force

