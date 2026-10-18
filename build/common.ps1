function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead((Resolve-Path $Path).Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
