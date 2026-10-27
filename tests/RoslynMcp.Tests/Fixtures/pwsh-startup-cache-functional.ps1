$ErrorActionPreference = 'Stop'
$repo = $env:OWNERSHIP_REPO
$launch = $env:OWNERSHIP_LAUNCH
$cache = Join-Path $env:XDG_CACHE_HOME 'powershell'
. (Join-Path $repo 'skills/mcp-server-surface-test/lib/render-finding.ps1')
$finding = @{ id = 'test-finding-id'; source_repo = 'sample-repo'; severity = 'P2'; area = 'tools'; server_version = '1.35.0'; anchors = @('z/late.cs:1', 'a/early.cs:2', 'm/middle.cs:3'); finding = 'X happens'; repro = 'do Y'; proposed_fix = 'fix Z' }
$body = Render-FindingBody -Finding $finding
for ($index = 0; $index -lt 100; $index++) {
    if ((Render-FindingBody -Finding $finding) -cne $body) { throw 'Renderer determinism failed.' }
}
$lines = $body.Split([char]10) | ForEach-Object { $_.TrimEnd([char]13) }
$a = [array]::IndexOf($lines, '  - a/early.cs:2')
$m = [array]::IndexOf($lines, '  - m/middle.cs:3')
$z = [array]::IndexOf($lines, '  - z/late.cs:1')
if ($a -le 0 -or $m -le $a -or $z -le $m) { throw 'Renderer anchor order failed.' }
foreach ($line in @('- finding: X happens', '- repro: do Y', '- proposed-fix: fix Z')) {
    if (-not $body.Contains($line)) { throw "Renderer missing $line" }
}
$decision = (& (Join-Path $repo 'eng/resolve-ci-topology.ps1') -EventName pull_request -ChangedFilesJson '[[{"filename":"src/Foo.cs"}]]' -ReportedChangedFileCount 1) | ConvertFrom-Json
if ($decision.docs_only -or $decision.runner_matrix.Count -ne 7 -or $decision.reason -cne 'Code PR: four hosted Windows and three hosted Linux shards.') { throw 'Topology failed.' }
$sibling = Join-Path $cache "functional-$launch.clixml"
[pscustomobject]@{ launch = $launch; body = $body } | Export-Clixml -LiteralPath $sibling
$loaded = Import-Clixml -LiteralPath $sibling
if ($loaded.launch -cne $launch -or $loaded.body -cne $body) { throw 'Sibling serialization failed.' }
$replacement = "$sibling.replacement"
[IO.File]::WriteAllText($replacement, 'replacement')
[IO.File]::Move($replacement, $sibling, $true)
if ([IO.File]::ReadAllText($sibling) -cne 'replacement') { throw 'Sibling replacement failed.' }
if ($launch -ceq '8') {
    $env:OWNERSHIP_LAUNCH = '8-child'
    & "$PSHOME/pwsh" -NoProfile -File $PSCommandPath
    $childExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $env:OWNERSHIP_OUTPUT 'descendant.exit'), [string]$childExit)
    if ($childExit -ne 0) { throw "Descendant failed: $childExit" }
}
if ($launch -ceq '1') {
    # Exercise ordinary module discovery/autoload and the real deferred native cache writer.
    Get-Module -ListAvailable | Out-Null
    $converted = ConvertTo-Json @{ module = 'autoload' } | ConvertFrom-Json
    if ($converted.module -cne 'autoload') { throw 'Module autoload failed.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $moduleFiles = @(Get-ChildItem -LiteralPath $cache -Filter 'ModuleAnalysisCache*' -File | Where-Object Length -GT 16)
        if ($moduleFiles.Count -gt 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($moduleFiles.Count -eq 0) { throw 'Native module analysis serialization exceeded fixed 30-second budget.' }
    # Native AnalysisCache serialization begins with the PSModuleCache signature.
    foreach ($file in $moduleFiles) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $signature = [Text.Encoding]::ASCII.GetString($bytes, 0, 13)
        if ($signature -cne 'PSMODULECACHE') { throw 'Unexpected native module-cache signature.' }
    }
    "MODULE_CACHE_PASS=$($moduleFiles.Count)"
}
[System.Runtime.ProfileOptimization]::StartProfile($null)
$profile = Get-Item -LiteralPath (Join-Path $cache 'StartupProfileData-NonInteractive')
if ($profile.Length -le 0) { throw 'Recorded profile missing.' }
[pscustomobject]@{ launch = $launch; pid = $PID; cache = $env:XDG_CACHE_HOME; render = 'PASS'; topology = 'PASS'; sibling = 'PASS'; profile = $profile.Name; telemetryPresent = (Test-Path -LiteralPath (Join-Path $cache 'telemetry.uuid')) } | ConvertTo-Json -Compress
