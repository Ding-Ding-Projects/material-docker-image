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
# Pack the complete source so the companion can build with no checkout nearby: every tracked and non-ignored file, plus the generated
# changelog (the unpacked copy has no git history to make it from), with a manifest of SHA-256 hashes the companion checks on unpack.
& "$PSScriptRoot/make-changelog.ps1"
if (-not (Test-Path -LiteralPath '.git') -and (Test-Path -LiteralPath 'EMBEDDED-SOURCE.json')) {
    # Rebuilding from the companion's own unpacked copy: pack exactly the files it was given.
    $sourceFiles = @((Get-Content -LiteralPath 'EMBEDDED-SOURCE.json' -Raw | ConvertFrom-Json).files.path | Sort-Object -Unique)
} else {
    $listed = @(& git ls-files -co --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory for the companion unavailable.' }
    $sourceFiles = @($listed | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }) + @('src/MaterialDockerImage/Assets/changelog.json') | Sort-Object -Unique
}
if ($sourceFiles.Count -eq 0) { throw 'Source inventory for the companion unavailable.' }
$sourceZip = Join-Path $root 'out/companion/source.zip'
if (Test-Path $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$manifestRows = [System.Collections.Generic.List[object]]::new()
$zipStream = [System.IO.File]::Open($sourceZip, 'CreateNew')
$archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $sourceFiles) {
        $name = $file -replace '\\', '/'
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $file), $name, [System.IO.Compression.CompressionLevel]::Optimal)
        $manifestRows.Add([ordered]@{path=$name; sha256=(Get-Sha256 (Join-Path $root $file)).ToLowerInvariant()})
    }
    $manifestJson = [ordered]@{source=$source; files=$manifestRows} | ConvertTo-Json -Depth 4
    $entry = $archive.CreateEntry('EMBEDDED-SOURCE.json')
    $writer = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
    $writer.Write($manifestJson); $writer.Dispose()
} finally { $archive.Dispose(); $zipStream.Dispose() }
$stage = Join-Path $root 'out/companion/publish'
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$started = [DateTime]::UtcNow.ToString('o')
# Native stderr must not become a terminating error under 'Stop'; the exit code decides.
$ErrorActionPreference = 'Continue'
& $dotnet publish src/BuildCompanion/BuildCompanion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true "-p:Version=$Version" "-p:InformationalVersion=$Version+$source" "-p:EmbeddedSourceZip=$sourceZip" -o $stage 2>&1 | Tee-Object out/logs/publish-companion.log
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
# Prove the published executable really carries this source: it reports the packed commit and exits without opening a window.
$probeFile = Join-Path $root 'out/companion/embedded-commit.txt'
if (Test-Path $probeFile) { Remove-Item -LiteralPath $probeFile -Force }
$probe = Start-Process -FilePath $target -ArgumentList @('--embedded-source-commit', "`"$probeFile`"") -Wait -PassThru
if ($probe.ExitCode -ne 0 -or -not (Test-Path $probeFile) -or (Get-Content $probeFile -Raw).Trim() -ne $source) { throw 'The published companion does not carry the packed source of this commit.' }
Remove-Item -LiteralPath $probeFile -Force
$zipItem = Get-Item $sourceZip
$item = Get-Item $target
@{source=$source; sourceBinding=$binding; version=$Version; startedUtc=$started; completedUtc=[DateTime]::UtcNow.ToString('o'); sdk=(& $dotnet --version); runtime='win-x64'; selfContained=$true; singleFile=$true; file=@{name=$item.Name; sha256=(Get-Sha256 $target); bytes=$item.Length}; embeddedSource=@{commit=$source; files=$sourceFiles.Count; zipSha256=(Get-Sha256 $sourceZip); zipBytes=$zipItem.Length}} | ConvertTo-Json -Depth 7 | Set-Content out/receipts/companion.json
if (-not $NoReleaseCopy -and (Test-Path out/releases)) { Copy-Item $target, out/receipts/companion.json out/releases -Force }
Write-Host "Companion ready: out/companion/MaterialDockerImageBuild.exe ($($item.Length) bytes)."
