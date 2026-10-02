param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [string]$Name,
    [switch]$Details,
    [switch]$AsJson,
    [switch]$UpdateManifest
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content (Join-Path $repositoryRoot 'docs/common-porting-manifest.json') -Raw | ConvertFrom-Json
# Tokenize strings and character literals too, so URLs and quoted comment markers
# do not become comments. The source inventory is pinned, not read from moving HEAD.
$tokenPattern = '(?s)@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
function Get-Comments([string]$source) {
    foreach ($token in [regex]::Matches($source, $tokenPattern)) {
        if ($token.Value.StartsWith('//') -or $token.Value.StartsWith('/*')) {
            [pscustomobject]@{
                text = $token.Value
                normalized = [regex]::Replace($token.Value, '\s+', ' ').Trim()
                line = 1 + ([regex]::Matches($source.Substring(0, $token.Index), '\n')).Count
            }
        }
    }
}

$results = foreach ($entry in $manifest.entries) {
    if ($Name -and [IO.Path]::GetFileNameWithoutExtension($entry.upstream) -ne $Name) { continue }
    $upstream = (git -C $UpstreamRoot show "$($manifest.baseline):$($entry.upstream)") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read $($entry.upstream) at the pinned commit." }
    $paths = @($entry.implementation) + @($entry.candidates) | Select-Object -Unique
    $local = ($paths | ForEach-Object {
        $path = Join-Path $repositoryRoot $_
        if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw }
    }) -join "`n"
    $counts = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($comment in @(Get-Comments $local)) {
        $count = if ($counts.ContainsKey($comment.normalized)) { $counts[$comment.normalized] } else { 0 }
        $counts[$comment.normalized] = 1 + $count
    }
    $comments = @(Get-Comments $upstream)
    $missing = @(
        foreach ($comment in $comments) {
            if ($counts.ContainsKey($comment.normalized) -and $counts[$comment.normalized] -gt 0) {
                $counts[$comment.normalized]--
            } else { $comment }
        }
    )
    [pscustomobject]@{
        upstream = $entry.upstream
        total = $comments.Count
        missing = $missing.Count
        local = $paths -join ', '
        missingComments = if ($Details) { $missing } else { @() }
    }
}
if ($UpdateManifest) {
    foreach ($result in $results) {
        $entry = $manifest.entries | Where-Object upstream -eq $result.upstream
        $entry | Add-Member -Force -NotePropertyName comments -NotePropertyValue ([ordered]@{
            upstreamCount = $result.total
            preservedCount = $result.total - $result.missing
            missingCount = $result.missing
        })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $repositoryRoot 'docs/common-porting-manifest.json') -Encoding utf8
}
if ($Details -or $AsJson) { $results | ConvertTo-Json -Depth 6 }
else { $results | Select-Object upstream, total, missing, local | Format-Table -AutoSize }
