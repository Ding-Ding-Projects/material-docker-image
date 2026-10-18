param([switch]$Silent)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content "$PSScriptRoot/dependencies.json" -Raw | ConvertFrom-Json
$tools = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $tools | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Get-Verified($Url, $Path, $Hash, $Algorithm = 'SHA256') {
    function Digest($file) {
        $stream = [IO.File]::OpenRead($file)
        $hashAlgorithm = [Security.Cryptography.HashAlgorithm]::Create($Algorithm)
        try { return ([BitConverter]::ToString($hashAlgorithm.ComputeHash($stream))).Replace('-', '') }
        finally { $stream.Dispose(); $hashAlgorithm.Dispose() }
    }
    if ((Test-Path $Path) -and (Digest $Path) -eq $Hash) { return }
    $partial = "$Path.download"
    Invoke-WebRequest $Url -UseBasicParsing -OutFile $partial -TimeoutSec 600
    if ((Digest $partial) -ne $Hash) { throw "Download digest mismatch: $Url" }
    Move-Item -LiteralPath $partial -Destination $Path -Force
}
$node = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $node) {
    $nodeExe = Join-Path $tools "node-v$($manifest.nodeVersion)-win-x64/node.exe"
    if (-not (Test-Path $nodeExe)) {
        Get-Verified $manifest.nodeUrl "$tools/node.zip" $manifest.nodeSha256
        Expand-Archive "$tools/node.zip" $tools -Force
    }
} else { $nodeExe = $node.Source }
if (-not (& $nodeExe --version)) { throw 'Node.js bootstrap did not provide a working runtime.' }
$nodeExe | Set-Content "$tools/node-path.txt"
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not $dotnet -or -not ((& $dotnet.Source --list-sdks) -match "^$([regex]::Escape($manifest.sdkVersion)) ")) {
    $dotnetPath = Join-Path $tools 'dotnet/dotnet.exe'
    if (-not (Test-Path $dotnetPath) -or (& $dotnetPath --list-sdks) -notmatch "^$([regex]::Escape($manifest.sdkVersion)) ") {
        Write-Host "Obtaining portable .NET SDK $($manifest.sdkVersion); no administrator rights or global PATH change required."
        Get-Verified $manifest.sdkUrl "$tools/sdk.zip" $manifest.sdkSha512 'SHA512'
        Expand-Archive "$tools/sdk.zip" "$tools/dotnet" -Force
    }
    $dotnetExe = $dotnetPath
} else { $dotnetExe = $dotnet.Source }
Get-Verified $manifest.nugetUrl "$tools/nuget.exe" $manifest.nugetSha256
$squirrelArchive = Join-Path $tools 'squirrel.windows.2.0.1.nupkg'
$cache = Join-Path $env:USERPROFILE '.nuget/packages/squirrel.windows/2.0.1/squirrel.windows.2.0.1.nupkg'
if ((Test-Path $cache) -and (Get-Sha256 $cache) -eq $manifest.squirrelSha256) { Copy-Item $cache $squirrelArchive -Force }
Get-Verified $manifest.squirrelUrl $squirrelArchive $manifest.squirrelSha256
Copy-Item $squirrelArchive "$tools/squirrel.zip" -Force
Expand-Archive "$tools/squirrel.zip" "$tools/squirrel" -Force
if (-not (Test-Path "$tools/squirrel/tools/Squirrel.exe")) { throw 'Squirrel tool extraction is incomplete.' }
if (-not ((& $dotnetExe --list-sdks) -match "^$([regex]::Escape($manifest.sdkVersion)) ")) { throw 'Required SDK unavailable after bootstrap.' }
$dotnetExe | Set-Content "$tools/dotnet-path.txt"
Write-Host "Ready: .NET SDK $($manifest.sdkVersion), NuGet $($manifest.nugetVersion), Squirrel.Windows $($manifest.squirrelVersion)."

