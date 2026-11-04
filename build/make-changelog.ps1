# Generates src/MaterialDockerImage/Assets/changelog.json from git log. Deterministic: the same history gives the same bytes.
# Entries are original subject lines grouped under the first tag that contains them; commits after the newest tag are "Unreleased".
param([string]$Output = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $root 'src/MaterialDockerImage/Assets/changelog.json' }
Set-Location $root

function Invoke-Git([string[]]$Arguments) {
    $text = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    return @($text)
}

$repository = 'Ding-Ding-Projects/material-docker-image'
$remote = (& git remote get-url origin 2>$null | Out-String).Trim()
if ($remote -match 'github\.com[:/]([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(\.git)?$') { $repository = $Matches[1] }

$tags = @(Invoke-Git @('tag', '--list', 'v*') |
    Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
    Sort-Object { [version]($_.Substring(1)) })

function Read-Entries([string]$Range) {
    $lines = Invoke-Git @('log', '--no-merges', '--format=%H%x09%cs%x09%s', $Range)
    foreach ($line in $lines) {
        if (-not $line) { continue }
        $parts = $line -split "`t", 3
        if ($parts[0] -notmatch '^[0-9a-f]{40}$') { throw "Unexpected git log line: $line" }
        [ordered]@{ sha = $parts[0]; date = $parts[1]; subject = $parts[2] }
    }
}

$groups = New-Object System.Collections.Generic.List[object]
$previous = $null
foreach ($tag in $tags) {
    $range = if ($previous) { "$previous..$tag" } else { $tag }
    $entries = @(Read-Entries $range)
    $date = @(Invoke-Git @('log', '-1', '--format=%cs', $tag))[0]
    $groups.Add([ordered]@{ version = $tag; date = $date; unreleased = $false; entries = $entries })
    $previous = $tag
}
if ($previous) {
    $tail = @(Read-Entries "$previous..HEAD")
    if ($tail.Count -gt 0) { $groups.Add([ordered]@{ version = "Unreleased since $previous"; date = ''; unreleased = $true; entries = $tail }) }
}
else {
    $all = @(Read-Entries 'HEAD')
    if ($all.Count -gt 0) { $groups.Add([ordered]@{ version = 'Unreleased'; date = ''; unreleased = $true; entries = $all }) }
}
# Newest first.
$ordered = @($groups.ToArray())
[array]::Reverse($ordered)
$total = ($ordered | ForEach-Object { $_.entries.Count } | Measure-Object -Sum).Sum
$document = [ordered]@{
    schema = 'material-docker-image/changelog'
    version = 1
    repository = $repository
    note = 'Generated at build time from this repository git history. Versions are git tags that existed when this build was made; releases published afterwards are not listed, and merge commits are omitted.'
    groups = $ordered
}
New-Item -ItemType Directory -Force (Split-Path $Output -Parent) | Out-Null
$json = $document | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($Output, $json, (New-Object Text.UTF8Encoding($false)))
Write-Host "Wrote $total changes in $($ordered.Count) groups to $Output"
