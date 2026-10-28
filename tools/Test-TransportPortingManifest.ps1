param([string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'))

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $repositoryRoot 'docs/transport-porting-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$head = git -C $UpstreamRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $manifest.baseline) { throw 'Unexpected upstream baseline.' }

function Get-NormalizedComments([string]$source) {
    # Skip literals before recognizing comment tokens; URLs inside strings are not comments.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|(?<comment>/\*.*?\*/|//[^\r\n]*)'
    foreach ($match in [regex]::Matches($source, $tokens)) {
        if (-not $match.Groups['comment'].Success) { continue }
        $comment = $match.Groups['comment'].Value -replace '^/\*+|\*/$|^//', ''
        $comment = $comment -replace '(?m)^\s*\* ?', ''
        ($comment -replace '\s+', ' ').Trim()
    }
}

$expected = @($manifest.entries | ForEach-Object { $_.upstream } | Sort-Object)
$actual = @(Get-ChildItem (Join-Path $UpstreamRoot 'transport/src') -Recurse -File |
    Where-Object { $_.Extension -eq '.java' -or $_.Name -in @('module-info.yml', 'hosts-unicode') } |
    ForEach-Object { $_.FullName.Substring((Resolve-Path $UpstreamRoot).Path.Length + 1).Replace('\', '/') } | Sort-Object)
if (Compare-Object $expected $actual) { throw 'Transport inventory changed.' }

$mappedCount = 0
foreach ($entry in $manifest.entries) {
    $path = Join-Path $UpstreamRoot $entry.upstream
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($entry.hashMode -eq 'utf8-lf') {
        $source = [Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
        $bytes = [Text.Encoding]::UTF8.GetBytes($source)
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($hash -ne $entry.sha256) { throw "Original source changed: $($entry.upstream)" }

    $implementation = ''
    foreach ($target in $entry.implementation) {
        $targetPath = Join-Path $repositoryRoot $target
        if (-not (Test-Path $targetPath -PathType Leaf)) { throw "Missing target: $target" }
        if ($entry.hashMode -eq 'binary') {
            $targetBytes = [IO.File]::ReadAllBytes($targetPath)
            if ([Convert]::ToBase64String($bytes) -ne [Convert]::ToBase64String($targetBytes)) { throw 'Test resource changed.' }
        } else {
            $implementation += Get-Content $targetPath -Raw -Encoding UTF8
        }
    }
    if ($entry.status -eq 'pending') { continue }
    if ($path.EndsWith('.java')) {
        $comments = @(Get-NormalizedComments $source | Select-Object -Unique)
        $ported = @(Get-NormalizedComments $implementation | Select-Object -Unique)
        foreach ($comment in $comments) {
            if ($comment -cnotin $ported) { throw "Missing original comment in $($entry.upstream): $comment" }
        }
        if ($comments.Count -ne $entry.comments.uniqueCount) { throw 'Comment inventory changed.' }
    }
    if ($entry.kind -eq 'test') {
        $methods = @([regex]::Matches($source, '@Test\s+(?:public\s+)?void\s+(\w+)') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object)
        $mapped = @($entry.caseMappings | ForEach-Object { $_.upstreamMethod } | Sort-Object)
        if (Compare-Object $methods $mapped) { throw "Test mapping changed: $($entry.upstream)" }
        foreach ($mapping in $entry.caseMappings) {
            if ($implementation -notmatch ('\[Fact\]\s+public void ' + [regex]::Escape($mapping.clrMethod) + '\(')) {
                throw "Missing original test body: $($mapping.clrMethod)"
            }
            $mappedCount++
        }
    }
}
Write-Output "Transport manifest verified: $($manifest.entries.Count) inventory entries, $mappedCount ported original tests, original comments preserved in reviewed entries. Pending entries remain unreviewed."
