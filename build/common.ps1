function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead((Resolve-Path $Path).Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
function Get-SourceBinding {
    $source = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'A committed source candidate is required.' }
    $paths = @(& git ls-files -co --exclude-standard -- src build global.json build.bat build-installer.bat download-dependencies.bat | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0) { throw 'Build input inventory unavailable.' }
    $entries = @($paths | ForEach-Object {
        if (-not (Test-Path -LiteralPath $_ -PathType Leaf)) { throw "Build input disappeared: $_" }
        @{path=$_; sha256=(Get-Sha256 $_)}
    })
    return @{source=$source; inputs=$entries}
}
function Assert-SourceBinding($Expected) {
    $actual = Get-SourceBinding
    $expectedRows = @($Expected.inputs | Sort-Object { $_.path } | ForEach-Object { "$($_.path)`t$($_.sha256)" })
    $actualRows = @($actual.inputs | Sort-Object { $_.path } | ForEach-Object { "$($_.path)`t$($_.sha256)" })
    if ($Expected.source -ne $actual.source -or ($expectedRows -join "`n") -ne ($actualRows -join "`n")) {
        throw 'Source candidate or build inputs changed; retain evidence and rebuild the pinned candidate.'
    }
}
function Assert-PayloadBinding($Receipt, [string]$Root) {
    $actualPaths = @(Get-ChildItem $Root -File -Recurse | ForEach-Object { $_.FullName.Substring((Resolve-Path $Root).Path.Length+1) } | Sort-Object)
    $expectedPaths = @($Receipt.files.path | Sort-Object)
    if (($actualPaths -join "`n") -ne ($expectedPaths -join "`n")) { throw 'Published payload inventory changed after build.' }
    foreach ($entry in $Receipt.files) {
        if ((Get-Sha256 (Join-Path $Root $entry.path)) -ne $entry.sha256) { throw "Published payload changed after build: $($entry.path)" }
    }
}
