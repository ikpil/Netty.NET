param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [string]$Name,
    [switch]$Details,
    [switch]$AsJson,
    [switch]$UpdateManifest,
    # Gate: every entry must have a finished, evidence-backed disposition (see the end of this script).
    [switch]$Check
)
# Finished dispositions. 'deprecated-not-ported' records an upstream @Deprecated type that the owner
# decided (2026-11-16) not to port; it needs a 'deprecation' object with 'javadoc' and 'replacement'.
$finishedStatuses = @('verified', 'clr-replacement', 'not-applicable', 'deprecated-not-ported')
$commentOptionalStatuses = @('clr-replacement', 'not-applicable', 'deprecated-not-ported')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content (Join-Path $repositoryRoot 'docs/common-porting-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
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
    $localComments = @($paths | ForEach-Object {
        $path = Join-Path $repositoryRoot $_
        if (Test-Path -LiteralPath $path) {
            $source = Get-Content -LiteralPath $path -Raw -Encoding UTF8
            if ([IO.Path]::GetExtension($path) -eq '.md') {
                # Markdown prose is not Java/C# source. Apostrophes and quotes
                # in prose must not swallow archived comments as string literals.
                # Tokenize each provenance block independently, just like a file.
                foreach ($block in [regex]::Matches($source, '(?ms)^```(?:java|csharp|cs)[ \t]*\r?\n(.*?)^```[ \t]*\r?$')) {
                    Get-Comments $block.Groups[1].Value
                }
            } else { Get-Comments $source }
        }
    })
    $counts = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($comment in $localComments) {
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
        required = $entry.status -notin $commentOptionalStatuses
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
            required = $result.required
            preservedCount = $result.total - $result.missing
            missingCount = $result.missing
        })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $repositoryRoot 'docs/common-porting-manifest.json') -Encoding utf8
}
if ($Check) {
    # Reject unfinished or incomplete entries. 'pending'/'in-progress' and unknown statuses fail;
    # every entry needs evidence; verified needs an implementation and every original comment;
    # replacements/exclusions need their reason (adaptation); deprecated-not-ported needs the
    # @Deprecated text and the replacement. The scoped -Name filter applies here too.
    # Inventory: every pinned file under the manifest scope has exactly one entry and every entry is such a file.
    # Since 2026-11-19 the scope is all of common/src (sources, templates, codegen script, resources, test data).
    $inventoryProblems = @()
    if (-not $Name) {
        $pinned = @(git -C $UpstreamRoot ls-tree -r --name-only $manifest.baseline -- @($manifest.scope))
        if ($LASTEXITCODE -ne 0) { throw 'Cannot list the pinned upstream scope.' }
        $pinnedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$pinned, [StringComparer]::Ordinal)
        $listed = @($manifest.entries | ForEach-Object { $_.upstream })
        $listedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $inventoryProblems = @(
            foreach ($path in $listed) {
                if (-not $listedSet.Add($path)) { "${path}: listed more than once" }
                if (-not $pinnedSet.Contains($path)) { "${path}: entry is not a pinned file under the manifest scope" }
            }
            foreach ($path in $pinned) {
                if (-not $listedSet.Contains($path)) { "${path}: pinned file under the manifest scope has no entry" }
            }
        )
    }
    $problems = @($inventoryProblems) + @(foreach ($result in $results) {
        $entry = $manifest.entries | Where-Object upstream -eq $result.upstream
        $id = $entry.upstream
        if ($entry.status -notin $finishedStatuses) { "${id}: status '$($entry.status)' is not a finished disposition"; continue }
        if (-not @(@($entry.evidence) | Where-Object { "$_".Trim() }).Count) { "${id}: evidence is empty" }
        switch ($entry.status) {
            'verified' {
                if (-not @($entry.implementation).Count) { "${id}: verified entry lists no implementation" }
                if ($result.missing -gt 0) { "${id}: verified entry misses $($result.missing) original comment(s)" }
            }
            'deprecated-not-ported' {
                $d = $entry.deprecation
                if (-not $d -or -not "$($d.javadoc)".Trim() -or -not "$($d.replacement)".Trim()) {
                    "${id}: deprecated-not-ported needs deprecation.javadoc and deprecation.replacement"
                }
            }
            default {
                if (-not "$($entry.adaptation)".Trim()) { "${id}: $($entry.status) needs its reason in adaptation" }
            }
        }
    })
    if ($problems.Count) {
        $problems | Write-Output
        Write-Output "$($problems.Count) problem(s) in $(@($results).Count) checked entries."
        exit 1
    }
    Write-Output "All $(@($results).Count) checked entries have a finished, evidence-backed disposition."
    exit 0
}
if ($Details -or $AsJson) { $results | ConvertTo-Json -Depth 6 }
else { $results | Select-Object upstream, total, missing, local | Format-Table -AutoSize }
