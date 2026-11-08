param(
    [Parameter(Mandatory)] [string]$Cache,
    [Parameter(Mandatory)] [ValidateRange(1, 30)] [int]$BudgetSeconds,
    [string]$ObservedMarkerPath
)

$ErrorActionPreference = 'Stop'
$deadline = [DateTime]::UtcNow.AddSeconds($BudgetSeconds)
$lastSharing = $null
$lastIncomplete = $null
while ([DateTime]::UtcNow -lt $deadline) {
    $moduleFiles = @(Get-ChildItem -LiteralPath $Cache -Filter 'ModuleAnalysisCache*' -File)
    $blocked = $false
    foreach ($file in $moduleFiles) {
        if ($ObservedMarkerPath -and -not (Test-Path -LiteralPath $ObservedMarkerPath)) {
            [IO.File]::WriteAllText($ObservedMarkerPath, 'observed')
        }
        $stream = $null
        try {
            # An exclusive read waits for the native writer to finish its cache file.
            $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        }
        catch {
            $failure = $_.Exception
            while ($failure.InnerException) { $failure = $failure.InnerException }
            $windowsSharing = ($failure.HResult -band 0xffff) -eq 32
            $unixSharing = -not $IsWindows -and $failure.HResult -eq 11
            if ($failure -isnot [IO.IOException] -or (-not $windowsSharing -and -not $unixSharing)) { throw }
            $lastSharing = "$($file.FullName): $($failure.Message) (HResult=$($failure.HResult))"
            $lastIncomplete = $null
            $blocked = $true
            break
        }
        try {
            if ($stream.Length -le 16) {
                $lastIncomplete = $file.FullName
                $lastSharing = $null
                $blocked = $true
                break
            }
            $reader = [IO.BinaryReader]::new($stream)
            $bytes = $reader.ReadBytes(13)
            if ($bytes.Length -ne 13 -or [Text.Encoding]::ASCII.GetString($bytes) -cne 'PSMODULECACHE') {
                throw "Unexpected native module-cache signature: $($file.FullName)"
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    if ($moduleFiles.Count -gt 0 -and -not $blocked -and [DateTime]::UtcNow -lt $deadline) {
        "MODULE_CACHE_PASS=$($moduleFiles.Count)"
        return
    }
    Start-Sleep -Milliseconds 100
}
if ($lastSharing) { throw "Native module analysis serialization exceeded fixed ${BudgetSeconds}-second budget; last sharing violation: $lastSharing" }
if ($lastIncomplete) { throw "Native module analysis serialization exceeded fixed ${BudgetSeconds}-second budget; incomplete candidate: $lastIncomplete" }
throw "Native module analysis serialization exceeded fixed ${BudgetSeconds}-second budget."
