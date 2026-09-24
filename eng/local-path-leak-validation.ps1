# local-user-path-leaks-sanitize-and-guard: this repository is public. Audit captures, retros and plan
# state are written on a developer machine and can carry that machine's user-profile path (and so the
# account name) into a commit. Reject any absolute user-profile path whose account segment is not a
# documented placeholder. Use `<user>`, `%USERPROFILE%`, `$env:USERPROFILE` or `~` instead.
# Dot-sourced by verify-ai-docs.ps1, which runs on every CI route (code, docs and evidence-lint).

# Account names that are documentation placeholders, never a real account on a maintainer machine.
$script:LocalPathPlaceholderNames = @(
    'foo', 'bar', 'baz', 'example', 'user', 'username', 'you', 'me', 'alice', 'bob',
    'public', 'default', 'runner', 'runneradmin'
)

$script:LocalPathLeakPatterns = @(
    # C:\Users\name, C:/Users/name, JSON-escaped C:\\Users\\name
    '(?<![A-Za-z])[A-Za-z]:(?:\\\\|\\|/)+Users(?:\\\\|\\|/)+(?<name>[A-Za-z0-9._-]+)'
    # Git Bash / MSYS form
    '(?<![A-Za-z0-9])/[a-z]/Users/(?<name>[A-Za-z0-9._-]+)'
    # Linux / macOS homes
    '(?<![A-Za-z0-9])/(?:home|Users)/(?<name>[A-Za-z0-9._-]+)/'
    # URL-encoded (resource URIs)
    '[A-Za-z]%3A(?:%2F|%5C)+Users(?:%2F|%5C)+(?<name>[A-Za-z0-9._-]+)'
    # Claude Code project-directory slugs derived from a profile path
    '(?<![A-Za-z0-9])[A-Za-z]--Users-(?<name>[A-Za-z0-9.]+)'
)

function Test-LocalPathLeak {
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Text)

    foreach ($pattern in $script:LocalPathLeakPatterns) {
        foreach ($match in [regex]::Matches($Text, $pattern, 'IgnoreCase')) {
            $name = $match.Groups['name'].Value
            if ($script:LocalPathPlaceholderNames -notcontains $name.ToLowerInvariant()) {
                return $match.Value
            }
        }
    }
    return $null
}

function Get-LocalPathLeakIssue {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.IO.FileInfo[]] $Files,
        [Parameter(Mandatory)][string] $RepoRoot
    )

    $issues = [System.Collections.Generic.List[string]]::new()

    # Self-test: a regex that stops matching would silently pass every file.
    $mustReject = @(
        'C:\Users\jdoe\AppData\Local\Temp\x',
        'C:/Users/jdoe/.claude/agents/a.md',
        '"log": "C:\\Users\\jdoe\\AppData\\Local\\Temp\\gate.log"',
        '/c/Users/jdoe/.claude',
        '/home/jdoe/src/repo',
        'roslyn://workspace/1/file/C%3A%2FUsers%2Fjdoe%2Frepo%2Fa.cs',
        'projects/C--Users-jdoe--claude/memory'
    )
    $mustAccept = @(
        'C:\Users\foo',
        '<user>/.nuget/packages/x',
        '%USERPROFILE%\actions-runner',
        'Join-Path $env:USERPROFILE ''actions-runner''',
        '/home/runner/work/Repo/Repo',
        '~/.claude/scripts/backlog.mjs',
        'https://github.com/darylmcd/Roslyn-Backed-MCP'
    )
    foreach ($sample in $mustReject) {
        if (-not (Test-LocalPathLeak -Text $sample)) {
            $issues.Add("Local-path leak guard failed to reject a known leak shape: $sample")
        }
    }
    foreach ($sample in $mustAccept) {
        $hit = Test-LocalPathLeak -Text $sample
        if ($hit) {
            $issues.Add("Local-path leak guard rejects a required placeholder form: $sample ($hit)")
        }
    }

    foreach ($file in $Files) {
        if ($file.FullName -eq $PSCommandPath -or $file.Name -eq 'local-path-leak-validation.ps1') {
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $probeLength = [Math]::Min($bytes.Length, 8000)
        if ([Array]::IndexOf($bytes, [byte]0, 0, $probeLength) -ge 0) {
            continue  # binary
        }

        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $leak = Test-LocalPathLeak -Text $text
        if ($leak) {
            $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
            $issues.Add("Local user-profile path in ${relative}: '$leak'. Replace it with <user>, %USERPROFILE%, `$env:USERPROFILE or ~.")
        }
    }

    return $issues
}
