[CmdletBinding()]
param(
    [ValidateRange(1, 1024)]
    [double]$MinimumAvailableGiB = 4,

    [ValidateRange(0, 1024)]
    [int]$MaximumDotnetProcessCount = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PhysicalMemorySnapshot {
    if ($IsWindows) {
        $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem
        return @{
            TotalBytes = [double]$operatingSystem.TotalVisibleMemorySize * 1KB
            AvailableBytes = [double]$operatingSystem.FreePhysicalMemory * 1KB
        }
    }

    if ($IsLinux) {
        $memoryValues = @{}
        foreach ($line in Get-Content -LiteralPath '/proc/meminfo') {
            if ($line -match '^(MemTotal|MemAvailable):\s+(\d+)\s+kB$') {
                $memoryValues[$Matches[1]] = [double]$Matches[2] * 1KB
            }
        }

        if (-not $memoryValues.ContainsKey('MemTotal') -or
            -not $memoryValues.ContainsKey('MemAvailable')) {
            throw 'Could not read MemTotal and MemAvailable from /proc/meminfo.'
        }

        return @{
            TotalBytes = $memoryValues.MemTotal
            AvailableBytes = $memoryValues.MemAvailable
        }
    }

    if ($IsMacOS) {
        $totalBytes = [double](& sysctl -n hw.memsize)
        $vmStat = & vm_stat
        $pageSizeLine = $vmStat | Select-Object -First 1
        if ($pageSizeLine -notmatch 'page size of (\d+) bytes') {
            throw 'Could not determine the macOS virtual-memory page size.'
        }

        $pageSize = [double]$Matches[1]
        $availablePages = 0.0
        foreach ($line in $vmStat) {
            if ($line -match '^Pages (free|inactive|speculative):\s+(\d+)\.$') {
                $availablePages += [double]$Matches[2]
            }
        }

        return @{
            TotalBytes = $totalBytes
            AvailableBytes = $availablePages * $pageSize
        }
    }

    throw 'Unsupported operating system for release-environment inspection.'
}

try {
    $memory = Get-PhysicalMemorySnapshot
    $minimumAvailableBytes = [Math]::Max(
        $MinimumAvailableGiB * 1GB,
        $memory.TotalBytes * 0.20)
    $processes = @(Get-Process -Name dotnet, testhost, MSBuild, VBCSCompiler -ErrorAction SilentlyContinue)
    $processCounts = @{
        dotnet = @($processes | Where-Object ProcessName -EQ 'dotnet').Count
        testhost = @($processes | Where-Object ProcessName -EQ 'testhost').Count
        MSBuild = @($processes | Where-Object ProcessName -EQ 'MSBuild').Count
        VBCSCompiler = @($processes | Where-Object ProcessName -EQ 'VBCSCompiler').Count
    }

    $availableGiB = [Math]::Round($memory.AvailableBytes / 1GB, 2)
    $requiredGiB = [Math]::Round($minimumAvailableBytes / 1GB, 2)
    $blockingWorkerCount =
        $processCounts.testhost + $processCounts.MSBuild + $processCounts.VBCSCompiler
    $healthy =
        $memory.AvailableBytes -ge $minimumAvailableBytes -and
        $blockingWorkerCount -eq 0 -and
        $processCounts.dotnet -le $MaximumDotnetProcessCount

    [pscustomobject]@{
        Status = if ($healthy) { 'ready' } else { 'refuse' }
        AvailableGiB = $availableGiB
        RequiredGiB = $requiredGiB
        Dotnet = $processCounts.dotnet
        Testhost = $processCounts.testhost
        MSBuild = $processCounts.MSBuild
        VBCSCompiler = $processCounts.VBCSCompiler
        MaximumDotnet = $MaximumDotnetProcessCount
    } | Format-List | Out-Host

    if (-not $healthy) {
        [Console]::Error.WriteLine(
            'Release verification environment is not isolated enough. ' +
            'Close the owning build/test sessions, run dotnet build-server shutdown, ' +
            'terminate only processes whose ownership you verified, and re-run this check. ' +
            'Do not use image-wide taskkill or killall commands.')
        exit 2
    }
}
catch {
    Write-Error "Release environment inspection failed: $($_.Exception.GetType().Name)"
    exit 1
}
