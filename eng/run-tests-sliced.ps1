<#
.SYNOPSIS
Slice the RoslynMcp.Tests suite into per-class `dotnet test --logger trx`
invocations, then accumulate results into a single triage-friendly report.

.DESCRIPTION
The full suite has 166 [TestClass] / 1,054 [TestMethod] tests against an
MSTest project that statically initializes ~50 services (TestBase) plus
MSBuild registration on every assembly load. A single monolithic
`dotnet test` blows the 25-min CI budget when slow integration classes
compound; this script runs each class in its own short-lived process so a
hung class can be killed without poisoning the whole run.

Output: artifacts/trx-slices/<UTC-timestamp>/ with per-class TRX files,
per-class stdout, per-failure stack files, summary.md / summary.json,
skipped-slices.log, and run-manifest.json (for resume support).

.EXAMPLE
pwsh eng/run-tests-sliced.ps1 -Filter '^Workspace.*' -MaxSlices 5

.EXAMPLE
pwsh eng/run-tests-sliced.ps1 -ExcludeNetwork

.EXAMPLE
pwsh eng/run-tests-sliced.ps1 -ResumeFrom artifacts/trx-slices/20260508T180000Z
#>
param(
    [string]$Configuration       = 'Release',
    [string]$OutputRoot          = 'artifacts/trx-slices',
    [int]   $SliceTimeoutSeconds = 180,
    [int]   $BlameHangSeconds    = 150,
    [string]$Filter,
    [switch]$ExcludeNetwork,
    [switch]$ExcludeIntegration,
    [int]   $MaxSlices,
    [string]$ResumeFrom,
    [switch]$Force,
    [switch]$NoBuild,
    [switch]$NoSampleRestore,
    [ValidateSet('Regex','ListTests')] [string]$DiscoveryMode = 'Regex'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

if ($BlameHangSeconds -ge $SliceTimeoutSeconds) {
    throw "BlameHangSeconds ($BlameHangSeconds) must be less than SliceTimeoutSeconds ($SliceTimeoutSeconds) so MSTest's soft kill fires before the PS-side hard kill."
}

# ---------- Helpers (shared with verify-release.ps1 / profile-large-solution.ps1) ----------

function Get-UtcStamp {
    return (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
}

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-GitValue([string]$RepoRoot, [string[]]$Arguments) {
    try {
        $output = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($output -join "`n").Trim()
        }
    } catch {
        return $null
    }
    return $null
}

function Invoke-DotnetStep([string]$Description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

# ---------- Discovery ----------

function Get-TestClasses {
    param([string]$TestRoot, [string]$Mode, [string]$Csproj, [string]$Configuration)

    if ($Mode -eq 'ListTests') {
        # Canonical-but-slow path. Parses `dotnet test --list-tests`, dedupes
        # to FQN class names by stripping the trailing .MethodName component.
        Write-Host "Discovering test classes via 'dotnet test --list-tests'..."
        $tmp = New-TemporaryFile
        try {
            & dotnet test $Csproj -c $Configuration --no-build --no-restore --nologo --list-tests *>$tmp
            $output = Get-Content -LiteralPath $tmp -Raw
        } finally {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
        $methods = ($output -split "`r?`n") |
            Where-Object { $_ -match '^\s{4,}[A-Za-z_][\w.]+\.[A-Za-z_]\w*$' } |
            ForEach-Object { $_.Trim() }
        $classes = @{}
        foreach ($m in $methods) {
            $lastDot = $m.LastIndexOf('.')
            if ($lastDot -lt 1) { continue }
            $fqn = $m.Substring(0, $lastDot)
            if (-not $classes.ContainsKey($fqn)) {
                $classes[$fqn] = $true
            }
        }
        return @($classes.Keys | Sort-Object | ForEach-Object {
            $lastDot = $_.LastIndexOf('.')
            [pscustomobject]@{
                Namespace = $_.Substring(0, $lastDot)
                ClassName = $_.Substring($lastDot + 1)
                Fqn       = $_
                FilePath  = $null
            }
        })
    }

    # Regex over *Tests.cs files. Fast and reliable for this repo's flat
    # 1:1 file-to-class layout. Skips bin/obj/TestResults.
    $files = Get-ChildItem -LiteralPath $TestRoot -Recurse -Filter '*Tests.cs' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults)[\\/]' }

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($f in $files) {
        $head = Get-Content -LiteralPath $f.FullName -TotalCount 200 -ErrorAction SilentlyContinue
        if (-not $head) { continue }
        $text = $head -join "`n"

        if ($text -notmatch '\[TestClass\]') { continue }

        $nsMatch = [regex]::Match($text, '(?m)^namespace\s+([\w.]+)')
        if (-not $nsMatch.Success) { continue }
        $ns = $nsMatch.Groups[1].Value

        # Class declaration following [TestClass] (allow attributes between).
        $classMatch = [regex]::Match(
            $text,
            '\[TestClass\][^{]*?\b(?:public\s+|internal\s+)?(?:partial\s+)?(?:sealed\s+|abstract\s+|static\s+)?class\s+(\w+)\b',
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )
        if (-not $classMatch.Success) { continue }
        $cls = $classMatch.Groups[1].Value

        if ($cls -match '<') {
            Write-Warning "Generic test class detected ($ns.$cls) — FullyQualifiedName filter may need adjustment."
        }

        $results.Add([pscustomobject]@{
            Namespace = $ns
            ClassName = $cls
            Fqn       = "$ns.$cls"
            FilePath  = $f.FullName
        })
    }
    return @($results | Sort-Object Fqn -Unique)
}

# ---------- Resume ----------

function Resume-PartialRun {
    param([object[]]$Classes, [string]$ResumeDir)

    $kept = @()
    $skipped = 0
    foreach ($c in $Classes) {
        $trxPath = Join-Path $ResumeDir "trx/$($c.ClassName)/$($c.ClassName).trx"
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            $kept += $c
            continue
        }
        try {
            $xml = [xml](Get-Content -LiteralPath $trxPath -Raw -ErrorAction Stop)
            $ns = New-Object System.Xml.XmlNamespaceManager $xml.NameTable
            $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $count = $xml.SelectNodes('//t:UnitTestResult', $ns).Count
            if ($count -ge 1) {
                $skipped++
            } else {
                $kept += $c
            }
        } catch {
            # Truncated / mid-write TRX → re-run.
            $kept += $c
        }
    }
    Write-Host "Resume: skipping $skipped class(es) with valid existing TRX; running $($kept.Count) remaining."
    return @($kept)
}

# ---------- Process tree kill ----------

function Stop-ProcessTree {
    param([int]$RootPid)

    $allProcs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue
    if (-not $allProcs) { return }

    $byParent = @{}
    foreach ($p in $allProcs) {
        if (-not $byParent.ContainsKey($p.ParentProcessId)) {
            $byParent[$p.ParentProcessId] = @()
        }
        $byParent[$p.ParentProcessId] += $p.ProcessId
    }

    $toKill = [System.Collections.Generic.List[int]]::new()
    $stack  = [System.Collections.Generic.Stack[int]]::new()
    $stack.Push($RootPid)
    while ($stack.Count -gt 0) {
        $cur = $stack.Pop()
        $toKill.Add($cur)
        if ($byParent.ContainsKey($cur)) {
            foreach ($child in $byParent[$cur]) { $stack.Push($child) }
        }
    }

    # Kill children first, root last.
    for ($i = $toKill.Count - 1; $i -ge 0; $i--) {
        try {
            Stop-Process -Id $toKill[$i] -Force -ErrorAction SilentlyContinue
        } catch {
            # Already exited.
        }
    }
}

# ---------- Slice invocation ----------

function Invoke-TestSlice {
    param(
        [pscustomobject]$Class,
        [string]$Csproj,
        [string]$Configuration,
        [string]$RunDir,
        [string]$BaseFilter,
        [int]$SliceTimeoutSeconds,
        [int]$BlameHangSeconds
    )

    $trxClassDir = Join-Path $RunDir "trx/$($Class.ClassName)"
    $stdoutDir   = Join-Path $RunDir 'stdout'
    New-Item -ItemType Directory -Force -Path $trxClassDir | Out-Null
    New-Item -ItemType Directory -Force -Path $stdoutDir   | Out-Null

    $outFile = Join-Path $stdoutDir "$($Class.ClassName).out.txt"
    $errFile = Join-Path $stdoutDir "$($Class.ClassName).err.txt"
    $trxFile = Join-Path $trxClassDir "$($Class.ClassName).trx"

    $sliceFilter = "FullyQualifiedName~$($Class.Fqn)&$BaseFilter"

    $args = @(
        'test', $Csproj,
        '-c', $Configuration,
        '--no-build', '--no-restore', '--nologo',
        '--filter', $sliceFilter,
        '--logger', "trx;LogFileName=$($Class.ClassName).trx",
        '--results-directory', $trxClassDir,
        "--blame-hang-timeout", "${BlameHangSeconds}s"
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList $args `
        -PassThru -NoNewWindow `
        -RedirectStandardOutput $outFile `
        -RedirectStandardError  $errFile

    $timedOut = $false
    try {
        Wait-Process -Id $proc.Id -Timeout $SliceTimeoutSeconds -ErrorAction Stop
    } catch {
        # Wait-Process throws on timeout. Verify the process really hasn't exited
        # before issuing the kill (a race could leave it complete).
        Start-Sleep -Milliseconds 100
        $proc.Refresh()
        if (-not $proc.HasExited) {
            $timedOut = $true
            Stop-ProcessTree -RootPid $proc.Id
            # Give Windows a beat to surface the exit before refreshing.
            Start-Sleep -Milliseconds 250
            $proc.Refresh()
            if (-not $proc.HasExited) {
                try { $proc.WaitForExit(2000) | Out-Null } catch { }
            }
        }
    }
    $sw.Stop()

    $exitCode = if ($proc.HasExited) { $proc.ExitCode } else { -1 }

    return [pscustomobject]@{
        ClassName    = $Class.ClassName
        Fqn          = $Class.Fqn
        ExitCode     = $exitCode
        TimedOut     = $timedOut
        DurationMs   = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        TrxPath      = $trxFile
        StdoutPath   = $outFile
        StderrPath   = $errFile
    }
}

# ---------- TRX parsing ----------

function Read-TrxResults {
    param([string]$TrxPath, [string]$ClassName, [string]$Fqn)

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        return [pscustomobject]@{
            Class       = $ClassName
            Fqn         = $Fqn
            Found       = $false
            Passed      = 0
            Failed      = 0
            NotExecuted = 0
            Other       = 0
            DurationMs  = 0
            Failures    = @()
        }
    }

    try {
        $xml = [xml](Get-Content -LiteralPath $TrxPath -Raw)
    } catch {
        return [pscustomobject]@{
            Class       = $ClassName
            Fqn         = $Fqn
            Found       = $false
            Passed      = 0
            Failed      = 0
            NotExecuted = 0
            Other       = 0
            DurationMs  = 0
            Failures    = @(@{ TestName = '<TRX-PARSE-ERROR>'; Message = $_.Exception.Message; Stack = ''; DurationMs = 0 })
        }
    }

    $ns = New-Object System.Xml.XmlNamespaceManager $xml.NameTable
    $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $passed = 0; $failed = 0; $skipped = 0; $other = 0
    $totalMs = 0.0
    $failures = [System.Collections.Generic.List[object]]::new()

    foreach ($r in $xml.SelectNodes('//t:UnitTestResult', $ns)) {
        $outcome = $r.GetAttribute('outcome')
        $testName = $r.GetAttribute('testName')
        $durStr = $r.GetAttribute('duration')

        $dMs = 0.0
        if ($durStr) {
            try {
                $ts = [TimeSpan]::Parse($durStr)
                $dMs = $ts.TotalMilliseconds
                $totalMs += $dMs
            } catch { }
        }

        switch ($outcome) {
            'Passed'      { $passed++ }
            'Failed'      {
                $failed++
                $msg = ''
                $stack = ''
                $msgNode = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
                if ($msgNode) { $msg = $msgNode.InnerText }
                $stackNode = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
                if ($stackNode) { $stack = $stackNode.InnerText }
                $failures.Add([pscustomobject]@{
                    TestName   = $testName
                    Message    = $msg
                    Stack      = $stack
                    DurationMs = [Math]::Round($dMs, 2)
                })
            }
            'NotExecuted' { $skipped++ }
            default       { $other++ }
        }
    }

    return [pscustomobject]@{
        Class       = $ClassName
        Fqn         = $Fqn
        Found       = $true
        Passed      = $passed
        Failed      = $failed
        NotExecuted = $skipped
        Other       = $other
        DurationMs  = [Math]::Round($totalMs, 2)
        Failures    = @($failures)
    }
}

function Write-FailureFile {
    param([string]$RunDir, [string]$ClassName, $Failure)
    $dir = Join-Path $RunDir 'failures'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $safeName = ($Failure.TestName -replace '[^\w.+-]', '_')
    if ($safeName.Length -gt 120) { $safeName = $safeName.Substring(0, 120) }
    $path = Join-Path $dir "$ClassName.$safeName.txt"
    $content = @(
        "Class:    $ClassName"
        "Test:     $($Failure.TestName)"
        "Duration: $($Failure.DurationMs) ms"
        ''
        '--- Message ---'
        $Failure.Message
        ''
        '--- Stack ---'
        $Failure.Stack
    ) -join "`n"
    Set-Content -LiteralPath $path -Value $content -Encoding UTF8
    return $path
}

# ---------- Reporting ----------

function Write-RunSummaryMarkdown {
    param(
        [string]$Path,
        [object]$Header,
        [object[]]$Results,
        [object[]]$SliceMeta,
        [object[]]$SkippedSlices
    )

    $totalPassed   = ($Results | Measure-Object -Property Passed      -Sum).Sum
    $totalFailed   = ($Results | Measure-Object -Property Failed      -Sum).Sum
    $totalSkipped  = ($Results | Measure-Object -Property NotExecuted -Sum).Sum
    $totalOther    = ($Results | Measure-Object -Property Other       -Sum).Sum
    $totalTests    = $totalPassed + $totalFailed + $totalSkipped + $totalOther
    $passRate      = if ($totalTests -gt 0) { [Math]::Round(100.0 * $totalPassed / $totalTests, 2) } else { 0 }

    $failedClasses = @($Results | Where-Object { $_.Failed -gt 0 } | Sort-Object -Property Failed -Descending)
    $slowest       = @($SliceMeta | Sort-Object -Property DurationMs -Descending | Select-Object -First 10)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Sliced Test Run — $($Header.RunId)")
    $lines.Add('')
    $lines.Add('| Field | Value |')
    $lines.Add('|---|---|')
    $lines.Add("| Started (UTC) | $($Header.StartedUtc) |")
    $lines.Add("| Wall clock | $($Header.WallClockSeconds) s |")
    $lines.Add("| Build duration | $($Header.BuildSeconds) s |")
    $lines.Add("| Git branch / HEAD | ``$($Header.GitBranch)`` / ``$($Header.GitHead)`` |")
    $lines.Add("| Configuration | $($Header.Configuration) |")
    $lines.Add("| Slice count | $($SliceMeta.Count) |")
    $lines.Add("| Slice timeout | $($Header.SliceTimeoutSeconds) s (blame-hang $($Header.BlameHangSeconds) s) |")
    $lines.Add("| Filter | $($Header.BaseFilter) |")
    $lines.Add('')
    $lines.Add('## Totals')
    $lines.Add('')
    $lines.Add('| Metric | Count |')
    $lines.Add('|---|---:|')
    $lines.Add("| Tests executed | $totalTests |")
    $lines.Add("| Passed | $totalPassed |")
    $lines.Add("| Failed | $totalFailed |")
    $lines.Add("| Not executed (skipped) | $totalSkipped |")
    $lines.Add("| Other outcome | $totalOther |")
    $lines.Add("| Pass rate | $passRate % |")
    $lines.Add("| Timed-out / crashed slices | $($SkippedSlices.Count) |")
    $lines.Add('')

    $lines.Add('## Failed Classes')
    $lines.Add('')
    if ($failedClasses.Count -eq 0) {
        $lines.Add('_None._')
    } else {
        $lines.Add('| Class | Failed | Total | Duration (ms) | First failure |')
        $lines.Add('|---|---:|---:|---:|---|')
        foreach ($c in $failedClasses) {
            $first = if ($c.Failures.Count -gt 0) { $c.Failures[0].TestName } else { '' }
            $total = $c.Passed + $c.Failed + $c.NotExecuted + $c.Other
            $lines.Add("| $($c.Class) | $($c.Failed) | $total | $($c.DurationMs) | ``$first`` |")
        }
    }
    $lines.Add('')

    $lines.Add('## Slowest 10 Slices')
    $lines.Add('')
    $lines.Add('| Class | Duration (ms) | Timed out | Exit code |')
    $lines.Add('|---|---:|:-:|---:|')
    foreach ($s in $slowest) {
        $to = if ($s.TimedOut) { 'YES' } else { '' }
        $lines.Add("| $($s.ClassName) | $($s.DurationMs) | $to | $($s.ExitCode) |")
    }
    $lines.Add('')

    $lines.Add('## Failures (grouped by class)')
    $lines.Add('')
    if ($failedClasses.Count -eq 0) {
        $lines.Add('_None._')
    } else {
        foreach ($c in $failedClasses) {
            $lines.Add("### $($c.Class)")
            $lines.Add('')
            foreach ($f in $c.Failures) {
                $msgFirst = if ($f.Message) {
                    $singleLine = ($f.Message -replace "`r?`n", ' ')
                    if ($singleLine.Length -gt 500) { $singleLine.Substring(0, 500) + '…' } else { $singleLine }
                } else { '' }
                $safeName = ($f.TestName -replace '[^\w.+-]', '_')
                if ($safeName.Length -gt 120) { $safeName = $safeName.Substring(0, 120) }
                $lines.Add("- **$($f.TestName)** ($($f.DurationMs) ms)")
                $lines.Add("    - $msgFirst")
                $lines.Add("    - See ``failures/$($c.Class).$safeName.txt``")
            }
            $lines.Add('')
        }
    }

    $lines.Add('## Timed-out / Crashed Slices')
    $lines.Add('')
    if ($SkippedSlices.Count -eq 0) {
        $lines.Add('_None._')
    } else {
        $lines.Add('| Class | Reason | Exit code | Duration (ms) |')
        $lines.Add('|---|---|---:|---:|')
        foreach ($s in $SkippedSlices) {
            $reason = if ($s.TimedOut) { 'timeout' } else { 'crash (exit >= 2)' }
            $lines.Add("| $($s.ClassName) | $reason | $($s.ExitCode) | $($s.DurationMs) |")
        }
    }

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

function Write-RunSummaryJson {
    param([string]$Path, [object]$Header, [object[]]$Results, [object[]]$SliceMeta, [object[]]$SkippedSlices)

    $totalPassed  = ($Results | Measure-Object -Property Passed      -Sum).Sum
    $totalFailed  = ($Results | Measure-Object -Property Failed      -Sum).Sum
    $totalSkipped = ($Results | Measure-Object -Property NotExecuted -Sum).Sum
    $totalOther   = ($Results | Measure-Object -Property Other       -Sum).Sum
    $totalTests   = $totalPassed + $totalFailed + $totalSkipped + $totalOther

    $payload = [pscustomobject]@{
        header  = $Header
        totals  = [pscustomobject]@{
            tests       = $totalTests
            passed      = $totalPassed
            failed      = $totalFailed
            notExecuted = $totalSkipped
            other       = $totalOther
            passRate    = if ($totalTests -gt 0) { [Math]::Round(100.0 * $totalPassed / $totalTests, 2) } else { 0 }
            sliceCount  = $SliceMeta.Count
            timedOutOrCrashed = $SkippedSlices.Count
        }
        results = $Results
        slices  = $SliceMeta
        skipped = $SkippedSlices
    }

    $payload | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
}

# ---------- Main ----------

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'RoslynMcp.slnx'
$sampleSolutionPath = Join-Path $repoRoot 'samples/SampleSolution/SampleSolution.slnx'
$testCsproj = Join-Path $repoRoot 'tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj'
$testRoot = Split-Path -Parent $testCsproj

foreach ($p in @($solutionPath, $testCsproj)) {
    if (-not (Test-Path -LiteralPath $p)) {
        throw "Required path not found: $p"
    }
}

# Resume guard.
$runDir = $null
$resumeManifest = $null
if ($ResumeFrom) {
    $resumeDir = if (Test-Path -LiteralPath $ResumeFrom -PathType Container) {
        Resolve-FullPath $ResumeFrom
    } else {
        Resolve-FullPath (Join-Path $repoRoot "$OutputRoot/$ResumeFrom")
    }
    if (-not (Test-Path -LiteralPath $resumeDir -PathType Container)) {
        throw "Resume directory not found: $resumeDir"
    }
    $manifestPath = Join-Path $resumeDir 'run-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Resume directory missing run-manifest.json: $manifestPath"
    }
    $resumeManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $currentHead = Get-GitValue -RepoRoot $repoRoot -Arguments @('rev-parse','HEAD')
    if ($resumeManifest.GitHeadFull -and $resumeManifest.GitHeadFull -ne $currentHead) {
        if (-not $Force) {
            throw "Resume aborted: git HEAD changed (was $($resumeManifest.GitHeadFull), now $currentHead). Pass -Force to override."
        }
        Write-Warning "Resuming with different git HEAD (was $($resumeManifest.GitHeadFull), now $currentHead). Forced."
    }
    $runDir = $resumeDir
} else {
    $runDir = Join-Path $repoRoot "$OutputRoot/$(Get-UtcStamp)"
}
$runDir = Resolve-FullPath $runDir

New-Item -ItemType Directory -Force -Path $runDir              | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'trx')      | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'stdout')   | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'failures') | Out-Null

Write-Host ''
Write-Host "Run directory: $runDir"
Write-Host ''

# Build once.
$buildSw = [System.Diagnostics.Stopwatch]::StartNew()
if (-not $NoBuild) {
    Write-Host "Restoring main solution: $solutionPath"
    & dotnet restore $solutionPath --nologo
    Invoke-DotnetStep 'dotnet restore (main solution)'

    if (-not $NoSampleRestore -and (Test-Path -LiteralPath $sampleSolutionPath)) {
        Write-Host "Restoring sample solution: $sampleSolutionPath"
        & dotnet restore $sampleSolutionPath --nologo
        Invoke-DotnetStep 'dotnet restore (sample solution)'
    }

    Write-Host "Building solution ($Configuration)..."
    & dotnet build $solutionPath -c $Configuration --no-restore --nologo
    Invoke-DotnetStep 'dotnet build'
}
$buildSw.Stop()

# Discover.
$allClasses = Get-TestClasses -TestRoot $testRoot -Mode $DiscoveryMode -Csproj $testCsproj -Configuration $Configuration
Write-Host "Discovered $($allClasses.Count) test class(es)."

if ($Filter) {
    $beforeCount = $allClasses.Count
    $allClasses = @($allClasses | Where-Object { $_.ClassName -match $Filter -or $_.Fqn -match $Filter })
    Write-Host "Filter '$Filter' kept $($allClasses.Count) of $beforeCount classes."
}
if ($MaxSlices -gt 0 -and $allClasses.Count -gt $MaxSlices) {
    Write-Host "MaxSlices=$MaxSlices applied (was $($allClasses.Count))."
    $allClasses = $allClasses | Select-Object -First $MaxSlices
}
if ($ResumeFrom) {
    $allClasses = Resume-PartialRun -Classes $allClasses -ResumeDir $runDir
}
if ($allClasses.Count -eq 0) {
    Write-Warning 'No classes to run after filters/resume. Exiting.'
    return
}

# Build base filter.
$baseFilterParts = @('TestCategory!=Benchmark')
if ($ExcludeNetwork)     { $baseFilterParts += 'TestCategory!=Network' }
if ($ExcludeIntegration) {
    $baseFilterParts += 'TestCategory!=Integration'
    $baseFilterParts += 'TestCategory!=Playwright'
    $baseFilterParts += 'TestCategory!=UI'
}
$baseFilter = $baseFilterParts -join '&'
Write-Host "Base filter: $baseFilter"
Write-Host ''

# Slice loop.
$startedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$runSw = [System.Diagnostics.Stopwatch]::StartNew()
$sliceMeta      = [System.Collections.Generic.List[object]]::new()
$results        = [System.Collections.Generic.List[object]]::new()
$skippedSlices  = [System.Collections.Generic.List[object]]::new()
$skippedLog     = Join-Path $runDir 'skipped-slices.log'
$idx = 0
foreach ($cls in $allClasses) {
    $idx++
    $pct = [int](($idx / $allClasses.Count) * 100)
    Write-Progress -Activity 'Running test slices' -Status "$idx / $($allClasses.Count) — $($cls.ClassName)" -PercentComplete $pct

    $meta = Invoke-TestSlice `
        -Class $cls `
        -Csproj $testCsproj `
        -Configuration $Configuration `
        -RunDir $runDir `
        -BaseFilter $baseFilter `
        -SliceTimeoutSeconds $SliceTimeoutSeconds `
        -BlameHangSeconds $BlameHangSeconds
    $sliceMeta.Add($meta)

    $isCrash = $meta.ExitCode -ge 2 -or $meta.ExitCode -lt 0
    if ($meta.TimedOut -or $isCrash) {
        $reason = if ($meta.TimedOut) { 'timeout' } else { "crash (exit=$($meta.ExitCode))" }
        Add-Content -LiteralPath $skippedLog -Value ("{0} {1} {2} duration={3}ms exit={4}" -f $startedUtc, $cls.ClassName, $reason, $meta.DurationMs, $meta.ExitCode)
        $skippedSlices.Add($meta)
        # Still attempt to parse TRX in case some results were written.
    }

    $r = Read-TrxResults -TrxPath $meta.TrxPath -ClassName $cls.ClassName -Fqn $cls.Fqn
    if ($r.Failures) {
        foreach ($f in $r.Failures) {
            Write-FailureFile -RunDir $runDir -ClassName $cls.ClassName -Failure $f | Out-Null
        }
    }
    $results.Add($r)

    $statusIcon = if ($meta.TimedOut) { '⏱' } elseif ($isCrash) { '✗' } elseif ($r.Failed -gt 0) { '✗' } else { '✓' }
    Write-Host ("  [{0}] {1} ({2} ms) — passed={3} failed={4} skipped={5}" -f $statusIcon, $cls.ClassName, $meta.DurationMs, $r.Passed, $r.Failed, $r.NotExecuted)
}
$runSw.Stop()
Write-Progress -Activity 'Running test slices' -Completed

# Write manifest + summaries (regenerate from union if resuming).
$header = [pscustomobject]@{
    RunId               = Split-Path -Leaf $runDir
    StartedUtc          = $startedUtc
    WallClockSeconds    = [Math]::Round($runSw.Elapsed.TotalSeconds, 2)
    BuildSeconds        = [Math]::Round($buildSw.Elapsed.TotalSeconds, 2)
    GitHead             = Get-GitValue -RepoRoot $repoRoot -Arguments @('rev-parse','--short','HEAD')
    GitHeadFull         = Get-GitValue -RepoRoot $repoRoot -Arguments @('rev-parse','HEAD')
    GitBranch           = Get-GitValue -RepoRoot $repoRoot -Arguments @('branch','--show-current')
    Configuration       = $Configuration
    SliceTimeoutSeconds = $SliceTimeoutSeconds
    BlameHangSeconds    = $BlameHangSeconds
    BaseFilter          = $baseFilter
    Filter              = $Filter
    ExcludeNetwork      = [bool]$ExcludeNetwork
    ExcludeIntegration  = [bool]$ExcludeIntegration
    DiscoveryMode       = $DiscoveryMode
    Resumed             = [bool]$ResumeFrom
}

# If resuming, union new results with prior TRX files for any classes we skipped.
if ($ResumeFrom) {
    $coveredClassNames = @{}
    foreach ($r in $results) { $coveredClassNames[$r.Class] = $true }

    # Re-discover ALL classes (not the trimmed list) so we re-read previously-completed TRX too.
    $fullList = Get-TestClasses -TestRoot $testRoot -Mode $DiscoveryMode -Csproj $testCsproj -Configuration $Configuration
    if ($Filter) {
        $fullList = @($fullList | Where-Object { $_.ClassName -match $Filter -or $_.Fqn -match $Filter })
    }
    foreach ($cls in $fullList) {
        if ($coveredClassNames.ContainsKey($cls.ClassName)) { continue }
        $trxPath = Join-Path $runDir "trx/$($cls.ClassName)/$($cls.ClassName).trx"
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) { continue }
        $r = Read-TrxResults -TrxPath $trxPath -ClassName $cls.ClassName -Fqn $cls.Fqn
        $results.Add($r)
    }
}

$manifestPath = Join-Path $runDir 'run-manifest.json'
$summaryMdPath   = Join-Path $runDir 'summary.md'
$summaryJsonPath = Join-Path $runDir 'summary.json'

$header | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-RunSummaryMarkdown -Path $summaryMdPath -Header $header -Results $results -SliceMeta $sliceMeta -SkippedSlices $skippedSlices
Write-RunSummaryJson     -Path $summaryJsonPath -Header $header -Results $results -SliceMeta $sliceMeta -SkippedSlices $skippedSlices

Write-Host ''
Write-Host '======================================================================'
Write-Host "Slice run complete in $($header.WallClockSeconds)s (build $($header.BuildSeconds)s)."
Write-Host "  Results:  $summaryMdPath"
Write-Host "  Manifest: $manifestPath"
if ($skippedSlices.Count -gt 0) {
    Write-Host "  WARNING: $($skippedSlices.Count) slice(s) timed out or crashed — see $skippedLog"
}
Write-Host '======================================================================'
