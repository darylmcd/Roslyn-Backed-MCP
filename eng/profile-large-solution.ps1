param(
    [string]$SolutionPath = "C:\Code-Repo\OrchardCore\OrchardCore.slnx",
    [string]$McpCommand = "roslynmcp",
    [string[]]$McpArguments = @(),
    [int]$Iterations = 5,
    [string]$SymbolQuery = "ContentItem",
    [int]$SymbolLimit = 50,
    [switch]$NoRestore,
    [switch]$RunEmitCompile,
    [string]$OutDir = ""
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function Get-UtcStamp {
    return (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
}

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $sorted.Count) { $index = $sorted.Count - 1 }
    return [Math]::Round([double]$sorted[$index], 2)
}

function Read-McpMessage([pscustomobject]$Client) {
    while ($true) {
        if ($Client.Process.HasExited) {
            $stderr = ""
            try {
                $stderr = $Client.StderrTask.GetAwaiter().GetResult()
            }
            catch {
                $stderr = ""
            }
            throw "MCP process exited before sending a response. Stderr: $stderr"
        }

        $line = $Client.Process.StandardOutput.ReadLine()
        if ($null -eq $line) {
            throw "Unexpected end of stream while reading MCP response."
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        return $line | ConvertFrom-Json
    }
}

function Send-McpMessage([pscustomobject]$Client, [hashtable]$Message) {
    $json = $Message | ConvertTo-Json -Depth 32 -Compress
    $Client.Process.StandardInput.WriteLine($json)
    $Client.Process.StandardInput.Flush()
}

function Start-McpClient {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Command
    foreach ($argument in $Arguments) {
        [void]$psi.ArgumentList.Add($argument)
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void]$process.Start()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $client = [pscustomobject]@{
        Process = $process
        NextId = 1
        StderrTask = $stderrTask
    }

    $initializeId = $client.NextId
    $client.NextId = $client.NextId + 1
    Send-McpMessage -Client $client -Message @{
        jsonrpc = "2.0"
        id = $initializeId
        method = "initialize"
        params = @{
            protocolVersion = "2024-11-05"
            capabilities = @{}
            clientInfo = @{
                name = "roslyn-mcp-large-solution-profiler"
                version = "1.0"
            }
        }
    }

    while ($true) {
        $response = Read-McpMessage -Client $client
        if ($response.PSObject.Properties.Name -contains "id" -and $response.id -eq $initializeId) {
            if ($response.PSObject.Properties.Name -contains "error") {
                throw "MCP initialize failed: $($response.error | ConvertTo-Json -Depth 8 -Compress)"
            }
            break
        }
    }

    Send-McpMessage -Client $client -Message @{
        jsonrpc = "2.0"
        method = "notifications/initialized"
        params = @{}
    }

    return $client
}

function Stop-McpClient([pscustomobject]$Client) {
    if ($null -eq $Client -or $null -eq $Client.Process -or $Client.Process.HasExited) {
        return
    }

    try {
        $Client.Process.StandardInput.Close()
    }
    catch {
        # Fall through to process cleanup.
    }
    finally {
        if (-not $Client.Process.WaitForExit(2000)) {
            $Client.Process.Kill($true)
            $Client.Process.WaitForExit()
        }
    }
}

function Invoke-McpRequest {
    param(
        [pscustomobject]$Client,
        [string]$Method,
        [hashtable]$Params
    )

    $id = $Client.NextId
    $Client.NextId = $Client.NextId + 1
    Send-McpMessage -Client $Client -Message @{
        jsonrpc = "2.0"
        id = $id
        method = $Method
        params = $Params
    }

    while ($true) {
        $response = Read-McpMessage -Client $Client
        if ($response.PSObject.Properties.Name -contains "id" -and $response.id -eq $id) {
            if ($response.PSObject.Properties.Name -contains "error") {
                throw ($response.error | ConvertTo-Json -Depth 12 -Compress)
            }
            return $response
        }
    }
}

function Invoke-McpTool {
    param(
        [pscustomobject]$Client,
        [string]$Name,
        [hashtable]$Arguments
    )

    return Invoke-McpRequest -Client $Client -Method "tools/call" -Params @{
        name = $Name
        arguments = $Arguments
    }
}

function Get-ToolText($Response) {
    if ($null -eq $Response.result.content) {
        return ""
    }

    foreach ($item in $Response.result.content) {
        if ($item.type -eq "text") {
            return [string]$item.text
        }
    }
    return ""
}

function Convert-ToolTextJson([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    try {
        return $Text | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-PropertyValue($Object, [string]$Name, $Default = $null) {
    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Get-ToolSummary([string]$ToolName, $Data, [string]$Text) {
    if ($null -eq $Data) {
        return @{ textLength = $Text.Length }
    }

    switch ($ToolName) {
        "server_info" {
            return @{
                version = Get-PropertyValue -Object $Data -Name "version"
                tools = Get-PropertyValue -Object (Get-PropertyValue -Object (Get-PropertyValue -Object $Data -Name "surface") -Name "registered") -Name "tools"
                parityOk = Get-PropertyValue -Object (Get-PropertyValue -Object (Get-PropertyValue -Object $Data -Name "surface") -Name "registered") -Name "parityOk"
            }
        }
        "workspace_load" {
            return @{
                workspaceId = Get-PropertyValue -Object $Data -Name "workspaceId"
                projectCount = Get-PropertyValue -Object $Data -Name "projectCount"
                documentCount = Get-PropertyValue -Object $Data -Name "documentCount"
                workspaceErrorCount = Get-PropertyValue -Object $Data -Name "workspaceErrorCount"
                restoreRequired = Get-PropertyValue -Object $Data -Name "restoreRequired"
            }
        }
        "workspace_status" {
            return @{
                isReady = Get-PropertyValue -Object $Data -Name "isReady"
                isStale = Get-PropertyValue -Object $Data -Name "isStale"
                workspaceErrorCount = Get-PropertyValue -Object $Data -Name "workspaceErrorCount"
                documentCount = Get-PropertyValue -Object $Data -Name "documentCount"
            }
        }
        "workspace_warm" {
            return @{
                coldCompilationCount = Get-PropertyValue -Object $Data -Name "coldCompilationCount"
                elapsedMs = Get-PropertyValue -Object $Data -Name "elapsedMs"
            }
        }
        "symbol_search" {
            $first = $null
            $symbols = Get-PropertyValue -Object $Data -Name "symbols"
            if ($symbols -and $symbols.Count -gt 0) {
                $first = Get-PropertyValue -Object $symbols[0] -Name "fullyQualifiedName"
            }
            return @{
                count = Get-PropertyValue -Object $Data -Name "count"
                first = $first
            }
        }
        "find_references" {
            return @{
                count = Get-PropertyValue -Object $Data -Name "count"
                totalCount = Get-PropertyValue -Object $Data -Name "totalCount"
                hasMore = Get-PropertyValue -Object $Data -Name "hasMore"
            }
        }
        "compile_check" {
            return @{
                count = Get-PropertyValue -Object $Data -Name "count"
                totalCount = Get-PropertyValue -Object $Data -Name "totalCount"
                errorCount = Get-PropertyValue -Object $Data -Name "errorCount"
                warningCount = Get-PropertyValue -Object $Data -Name "warningCount"
                hasMore = Get-PropertyValue -Object $Data -Name "hasMore"
            }
        }
        default {
            return @{ textLength = $Text.Length }
        }
    }
}

function Measure-McpTool {
    param(
        [pscustomobject]$Client,
        [string]$Operation,
        [string]$ToolName,
        [hashtable]$Arguments,
        [int]$Iteration
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-McpTool -Client $Client -Name $ToolName -Arguments $Arguments
        $sw.Stop()
        $text = Get-ToolText -Response $response
        $data = Convert-ToolTextJson -Text $text
        $isError = $false
        if ($response.result.PSObject.Properties.Name -contains "isError") {
            $isError = [bool]$response.result.isError
        }
        return [pscustomobject]@{
            Measurement = [pscustomobject]@{
                operation = $Operation
                tool = $ToolName
                iteration = $Iteration
                elapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
                ok = -not $isError
                error = $null
                summary = Get-ToolSummary -ToolName $ToolName -Data $data -Text $text
            }
            Data = $data
            Text = $text
        }
    }
    catch {
        $sw.Stop()
        return [pscustomobject]@{
            Measurement = [pscustomobject]@{
                operation = $Operation
                tool = $ToolName
                iteration = $Iteration
                elapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
                ok = $false
                error = $_.Exception.Message
                summary = @{}
            }
            Data = $null
            Text = ""
        }
    }
}

function Get-GitValue([string]$RepoRoot, [string[]]$Arguments) {
    try {
        $output = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($output -join "`n").Trim()
        }
    }
    catch {
        return $null
    }
    return $null
}

function Get-SolutionStats([string]$ResolvedSolutionPath) {
    $repoRoot = Split-Path -Parent $ResolvedSolutionPath
    $gitRoot = Get-GitValue -RepoRoot $repoRoot -Arguments @("rev-parse", "--show-toplevel")
    if ([string]::IsNullOrWhiteSpace($gitRoot)) {
        $gitRoot = $repoRoot
    }

    $solutionText = Get-Content -LiteralPath $ResolvedSolutionPath -Raw
    $solutionProjectCount = ([regex]::Matches($solutionText, "\.csproj")).Count
    $repoProjectCount = (Get-ChildItem -LiteralPath $gitRoot -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue | Measure-Object).Count
    $trackedFiles = @((Get-GitValue -RepoRoot $gitRoot -Arguments @("ls-files")) -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $trackedCSharp = @($trackedFiles | Where-Object { $_ -like "*.cs" })

    return [pscustomobject]@{
        solutionPath = $ResolvedSolutionPath
        repoRoot = $gitRoot
        solutionProjectCount = $solutionProjectCount
        repoProjectCount = $repoProjectCount
        trackedFileCount = $trackedFiles.Count
        trackedCSharpFileCount = $trackedCSharp.Count
        gitHead = Get-GitValue -RepoRoot $gitRoot -Arguments @("rev-parse", "--short", "HEAD")
        gitBranch = Get-GitValue -RepoRoot $gitRoot -Arguments @("branch", "--show-current")
        gitStatus = Get-GitValue -RepoRoot $gitRoot -Arguments @("status", "--short", "--branch")
    }
}

function New-SummaryRows($Measurements) {
    $rows = @()
    foreach ($group in ($Measurements | Group-Object operation)) {
        $okRows = @($group.Group | Where-Object { $_.ok })
        $values = [double[]]@($okRows | ForEach-Object { [double]$_.elapsedMs })
        $rows += [pscustomobject]@{
            operation = $group.Name
            attempts = $group.Count
            ok = $okRows.Count
            failed = $group.Count - $okRows.Count
            minMs = if ($values.Count -gt 0) { [Math]::Round(($values | Measure-Object -Minimum).Minimum, 2) } else { $null }
            p50Ms = Get-Percentile -Values $values -Percentile 50
            p95Ms = Get-Percentile -Values $values -Percentile 95
            p99Ms = Get-Percentile -Values $values -Percentile 99
            maxMs = if ($values.Count -gt 0) { [Math]::Round(($values | Measure-Object -Maximum).Maximum, 2) } else { $null }
        }
    }
    return @($rows | Sort-Object operation)
}

function Write-MarkdownReport {
    param(
        [string]$Path,
        $Metadata,
        $SummaryRows,
        $Measurements
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $solutionPath = $Metadata.solution.solutionPath
    $repoRoot = $Metadata.solution.repoRoot
    $gitBranch = $Metadata.solution.gitBranch
    $gitHead = $Metadata.solution.gitHead
    $symbolQueryText = $Metadata.symbolQuery
    $mcpCommandLineText = $Metadata.mcpCommandLine

    $lines.Add("# Large-solution profiling run")
    $lines.Add("")
    $lines.Add("| Field | Value |")
    $lines.Add("|---|---|")
    $lines.Add("| Date | $($Metadata.createdAtUtc) |")
    $lines.Add("| Solution | ``$solutionPath`` |")
    $lines.Add("| Repo | ``$repoRoot`` |")
    $lines.Add("| Git | ``$gitBranch`` / ``$gitHead`` |")
    $lines.Add("| Solution projects | $($Metadata.solution.solutionProjectCount) |")
    $lines.Add("| Repo projects | $($Metadata.solution.repoProjectCount) |")
    $lines.Add("| Tracked C# files | $($Metadata.solution.trackedCSharpFileCount) |")
    $lines.Add("| Iterations | $($Metadata.iterations) |")
    $lines.Add("| Symbol query | ``$symbolQueryText`` |")
    $lines.Add("| Restore | $($Metadata.restoreMode) |")
    $lines.Add("| MCP command | ``$mcpCommandLineText`` |")
    $lines.Add("")
    $lines.Add("## Timings")
    $lines.Add("")
    $lines.Add("| Operation | Attempts | OK | Failed | Min ms | P50 ms | P95 ms | P99 ms | Max ms |")
    $lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|---:|")
    foreach ($row in $SummaryRows) {
        $lines.Add("| $($row.operation) | $($row.attempts) | $($row.ok) | $($row.failed) | $($row.minMs) | $($row.p50Ms) | $($row.p95Ms) | $($row.p99Ms) | $($row.maxMs) |")
    }
    $lines.Add("")
    $lines.Add("## Failed Calls")
    $lines.Add("")
    $failed = @($Measurements | Where-Object { -not $_.ok })
    if ($failed.Count -eq 0) {
        $lines.Add("None.")
    }
    else {
        foreach ($item in $failed) {
            $lines.Add("- `$($item.operation)` iteration $($item.iteration): $($item.error)")
        }
    }
    $lines.Add("")
    $lines.Add("## Decision Notes")
    $lines.Add("")
    $lines.Add("- Compare P95 values with `docs/large-solution-profiling-baseline.md`.")
    $lines.Add("- Open performance work only with measured P95, operation name, and this solution profile.")

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

if ($Iterations -lt 1) {
    throw "Iterations must be at least 1."
}

$resolvedSolution = Resolve-FullPath -Path $SolutionPath
if (-not (Test-Path -LiteralPath $resolvedSolution -PathType Leaf)) {
    throw "Solution path not found: $resolvedSolution"
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Resolve-FullPath -Path ".") ("artifacts\large-solution-profiling\$(Get-UtcStamp)")
}
$OutDir = Resolve-FullPath -Path $OutDir
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$solutionStats = Get-SolutionStats -ResolvedSolutionPath $resolvedSolution
$measurements = [System.Collections.Generic.List[object]]::new()
$client = $null

$restoreMeasurement = $null
if (-not $NoRestore) {
    $restoreSw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet restore $resolvedSolution --nologo
    $restoreExitCode = $LASTEXITCODE
    $restoreSw.Stop()
    $restoreMeasurement = [pscustomobject]@{
        operation = "dotnet_restore"
        tool = "dotnet restore"
        iteration = 1
        elapsedMs = [Math]::Round($restoreSw.Elapsed.TotalMilliseconds, 2)
        ok = $restoreExitCode -eq 0
        error = if ($restoreExitCode -eq 0) { $null } else { "dotnet restore exited $restoreExitCode" }
        summary = @{}
    }
    $measurements.Add($restoreMeasurement)
    if ($restoreExitCode -ne 0) {
        throw "dotnet restore failed with exit code $restoreExitCode."
    }
}

try {
    $client = Start-McpClient -Command $McpCommand -Arguments $McpArguments
    $serverInfo = Measure-McpTool -Client $client -Operation "server_info" -ToolName "server_info" -Arguments @{} -Iteration 1
    $measurements.Add($serverInfo.Measurement)

    for ($i = 1; $i -le $Iterations; $i++) {
        $load = Measure-McpTool -Client $client -Operation "workspace_load" -ToolName "workspace_load" -Arguments @{
            path = $resolvedSolution
            autoRestore = $false
            verbose = $false
        } -Iteration $i
        $measurements.Add($load.Measurement)

        if ($load.Measurement.ok -and $null -ne $load.Data -and $load.Data.workspaceId) {
            $status = Measure-McpTool -Client $client -Operation "workspace_status_after_load" -ToolName "workspace_status" -Arguments @{
                workspaceId = [string]$load.Data.workspaceId
            } -Iteration $i
            $measurements.Add($status.Measurement)
            [void](Invoke-McpTool -Client $client -Name "workspace_close" -Arguments @{ workspaceId = [string]$load.Data.workspaceId })
        }
    }

    $activeLoad = Measure-McpTool -Client $client -Operation "workspace_load_active" -ToolName "workspace_load" -Arguments @{
        path = $resolvedSolution
        autoRestore = $false
        verbose = $false
    } -Iteration 1
    $measurements.Add($activeLoad.Measurement)
    if (-not $activeLoad.Measurement.ok -or $null -eq $activeLoad.Data -or -not $activeLoad.Data.workspaceId) {
        throw "Unable to load active workspace for semantic operation profiling."
    }
    $workspaceId = [string]$activeLoad.Data.workspaceId

    for ($i = 1; $i -le $Iterations; $i++) {
        $warm = Measure-McpTool -Client $client -Operation "workspace_warm" -ToolName "workspace_warm" -Arguments @{
            workspaceId = $workspaceId
            projects = @()
        } -Iteration $i
        $measurements.Add($warm.Measurement)
    }

    $selectedSymbolHandle = $null
    $selectedSymbol = $null
    for ($i = 1; $i -le $Iterations; $i++) {
        $search = Measure-McpTool -Client $client -Operation "symbol_search" -ToolName "symbol_search" -Arguments @{
            workspaceId = $workspaceId
            query = $SymbolQuery
            limit = $SymbolLimit
        } -Iteration $i
        $measurements.Add($search.Measurement)

        if ($null -eq $selectedSymbolHandle -and $search.Measurement.ok -and $null -ne $search.Data -and $search.Data.symbols) {
            foreach ($symbol in $search.Data.symbols) {
                if ($symbol.symbolHandle) {
                    $selectedSymbolHandle = [string]$symbol.symbolHandle
                    $selectedSymbol = $symbol
                    break
                }
            }
        }
    }

    if ($selectedSymbolHandle) {
        for ($i = 1; $i -le $Iterations; $i++) {
            $refs = Measure-McpTool -Client $client -Operation "find_references" -ToolName "find_references" -Arguments @{
                workspaceId = $workspaceId
                symbolHandle = $selectedSymbolHandle
                summary = $true
                limit = 100
            } -Iteration $i
            $measurements.Add($refs.Measurement)
        }
    }
    else {
        $measurements.Add([pscustomobject]@{
            operation = "find_references"
            tool = "find_references"
            iteration = 0
            elapsedMs = 0
            ok = $false
            error = "No symbolHandle found from symbol_search query '$SymbolQuery'."
            summary = @{}
        })
    }

    for ($i = 1; $i -le $Iterations; $i++) {
        $compile = Measure-McpTool -Client $client -Operation "compile_check_no_emit" -ToolName "compile_check" -Arguments @{
            workspaceId = $workspaceId
            emitValidation = $false
            severity = "Error"
            limit = 10
        } -Iteration $i
        $measurements.Add($compile.Measurement)
    }

    if ($RunEmitCompile) {
        for ($i = 1; $i -le $Iterations; $i++) {
            $compileEmit = Measure-McpTool -Client $client -Operation "compile_check_emit" -ToolName "compile_check" -Arguments @{
                workspaceId = $workspaceId
                emitValidation = $true
                severity = "Error"
                limit = 10
            } -Iteration $i
            $measurements.Add($compileEmit.Measurement)
        }
    }
}
finally {
    Stop-McpClient -Client $client
}

$mcpCommandLine = @($McpCommand) + @($McpArguments) -join " "
$metadata = [pscustomobject]@{
    createdAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    solution = $solutionStats
    iterations = $Iterations
    symbolQuery = $SymbolQuery
    symbolLimit = $SymbolLimit
    selectedReferenceSymbol = if ($selectedSymbol) { $selectedSymbol.fullyQualifiedName } else { $null }
    restoreMode = if ($NoRestore) { "skipped" } else { "dotnet restore before MCP profiling" }
    mcpCommandLine = $mcpCommandLine
}

$summaryRows = New-SummaryRows -Measurements $measurements
$result = [pscustomobject]@{
    metadata = $metadata
    summary = $summaryRows
    measurements = $measurements
}

$jsonPath = Join-Path $OutDir "profile-results.json"
$mdPath = Join-Path $OutDir "profile-report.md"
$result | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-MarkdownReport -Path $mdPath -Metadata $metadata -SummaryRows $summaryRows -Measurements $measurements

Write-Host "Wrote profiling JSON: $jsonPath"
Write-Host "Wrote profiling report: $mdPath"
