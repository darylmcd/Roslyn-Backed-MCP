#!/usr/bin/env bash
# A job owns one normally recorded startup input; later ordinary readers cannot rewrite it.
set -euo pipefail

[[ $(uname -s) == Linux ]] || { echo 'PowerShell cache initialization requires Linux.' >&2; exit 1; }
[[ $(id -u) != 0 ]] || { echo 'PowerShell cache initialization requires a nonroot runner.' >&2; exit 1; }
: "${RUNNER_TEMP:?RUNNER_TEMP must name the native job temporary directory}"
: "${GITHUB_ENV:?GITHUB_ENV must name the job environment file}"
[[ $RUNNER_TEMP == /* && -d $RUNNER_TEMP && -w $RUNNER_TEMP ]] || { echo 'RUNNER_TEMP must be an absolute writable directory.' >&2; exit 1; }
pwsh_binary=$(type -P pwsh)
pwsh_binary=$(readlink -f "$pwsh_binary")
[[ -x $pwsh_binary ]] || { echo 'The selected PowerShell binary is not executable.' >&2; exit 1; }

# mktemp creates the root privately before any native PowerShell process starts.
umask 077
cache_root=$(mktemp -d "$RUNNER_TEMP/pwsh-startup-cache.XXXXXXXXXX")
mkdir -m 700 "$cache_root/powershell"
XDG_CACHE_HOME="$cache_root" "$pwsh_binary" -NoProfile -NonInteractive -Command '
    $ErrorActionPreference = "Stop"
    $value = [pscustomobject]@{ seed = "job-owned"; values = @(3, 1, 2) }
    $roundtrip = $value | ConvertTo-Json -Depth 3 | ConvertFrom-Json
    if ($roundtrip.seed -cne "job-owned" -or ($roundtrip.values | Sort-Object) -join "," -cne "1,2,3") {
        throw "Startup seed functional check failed."
    }
    [System.Runtime.ProfileOptimization]::StartProfile($null)
'
profile="$cache_root/powershell/StartupProfileData-NonInteractive"
[[ -f $profile && ! -L $profile && -s $profile ]] || { echo 'PowerShell did not record a regular nonempty startup profile.' >&2; exit 1; }
chmod 444 "$profile"
printf 'XDG_CACHE_HOME=%s\n' "$cache_root" >> "$GITHUB_ENV"
# Runner teardown owns this root after every descendant has finished.
