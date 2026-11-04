param([switch]$NoReleaseCopy)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
New-Item -ItemType Directory -Force out/logs,out/receipts,out/docs | Out-Null
$nodePath = Join-Path $root '.tools/node-path.txt'
$node = if (Test-Path $nodePath) { (Get-Content $nodePath -Raw).Trim() } else { (Get-Command node -ErrorAction Stop).Source }
$binding = Get-SourceBinding
$started = [DateTime]::UtcNow.ToString('o')
$site = Join-Path $root 'out/docs/site'
$zip = Join-Path $root 'out/docs/MaterialDockerImageDocs.zip'
foreach ($stale in @($zip)) { if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force } }
$ErrorActionPreference = 'Continue'
& $node website/render.mjs --offline out/docs/site
$exitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($exitCode -ne 0) { exit $exitCode }
if (-not (Test-Path (Join-Path $site 'index.html'))) { throw 'Offline documentation home page was not produced.' }
# The package must work from file://, so it may not reference any remote asset.
$page = Get-Content (Join-Path $site 'index.html') -Raw -Encoding UTF8
if ($page -match '(?i)(?:src|srcset|poster)\s*=\s*["'']https?:' -or $page -match '(?i)<link[^>]+href\s*=\s*["'']https?:' -or $page -match '(?i)url\(\s*["'']?https?:' -or $page -match '(?i)@import\s+(?:url\(|["''])') { throw 'The offline documentation references a remote asset.' }
# Fixed entry order and timestamps keep the archive reproducible for one source candidate.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fixed = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$files = @(Get-ChildItem $site -File -Recurse | Sort-Object { $_.FullName.Substring($site.Length + 1).Replace('\', '/') })
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $name = $file.FullName.Substring($site.Length + 1).Replace('\', '/')
        $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixed
        $input = [IO.File]::OpenRead($file.FullName)
        $output = $entry.Open()
        try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
    }
} finally { $archive.Dispose() }
Assert-SourceBinding $binding
$item = Get-Item $zip
$provenance = Get-Content website/provenance.json -Raw | ConvertFrom-Json
@{source=$binding.source; sourceBinding=$binding; documentationVersion=$provenance.version; documentationUpdatedAt=$provenance.updatedAt; startedUtc=$started; completedUtc=[DateTime]::UtcNow.ToString('o'); offline=$true; fileCount=$files.Count; file=@{name=$item.Name; bytes=$item.Length; sha256=(Get-Sha256 $zip)}} | ConvertTo-Json -Depth 8 | Set-Content out/receipts/docs.json
if (-not $NoReleaseCopy -and (Test-Path out/releases)) { Copy-Item $zip, out/receipts/docs.json out/releases -Force }
Write-Host "Offline documentation ready: out/docs/MaterialDockerImageDocs.zip ($($item.Length) bytes, $($files.Count) files)."
