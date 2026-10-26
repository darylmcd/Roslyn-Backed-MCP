#!/usr/bin/env bash
# Fixed native regression: run as an ordinary Linux user on native storage.
set -euo pipefail
repo=$1
output=$2
mode=${3:-owned}
[[ $(id -u) != 0 ]] || { echo 'Ownership proof requires nonroot.' >&2; exit 1; }
mkdir -p "$output"
export RUNNER_TEMP="$output"
export GITHUB_ENV="$output/github-env"
: > "$GITHUB_ENV"
if [[ $mode == old ]]; then
    # Old workflow lifetime: the runtime records its normal writable startup input.
    export XDG_CACHE_HOME
    XDG_CACHE_HOME=$(mktemp -d "$RUNNER_TEMP/old-cache.XXXXXXXXXX")
    pwsh -NoProfile -NonInteractive -Command '$ErrorActionPreference="Stop"; if ((1+2) -ne 3) { throw "seed" }; [System.Runtime.ProfileOptimization]::StartProfile($null)'
else
    bash "$repo/eng/initialize-pwsh-startup-cache.sh"
    IFS= read -r assignment < "$GITHUB_ENV"
    [[ $assignment == XDG_CACHE_HOME=/* ]] || { echo 'Missing persisted cache root.' >&2; exit 1; }
    export "$assignment"
fi
profile="$XDG_CACHE_HOME/powershell/StartupProfileData-NonInteractive"
[[ -f $profile && -s $profile && ! -L $profile ]]
stat -c '%u %g %a %s %n' "$XDG_CACHE_HOME" "$XDG_CACHE_HOME/powershell" "$profile" > "$output/permissions.txt"
# Harmless open/close only. A permitted existing-file write-open is the old-code RED.
if (exec 3>> "$profile") 2> "$output/write-open.log"; then
    echo 'OWNERSHIP_REGRESSION: ordinary write-open permitted after valid normal seed.' >&2
    exit 1
fi
[[ $(stat -c %a "$XDG_CACHE_HOME") == 700 ]]
[[ $(stat -c %a "$XDG_CACHE_HOME/powershell") == 700 ]]
[[ $(stat -c %a "$profile") == 444 ]]
sha256sum "$profile" > "$output/before.sha256"
export OWNERSHIP_REPO="$repo" OWNERSHIP_OUTPUT="$output"
functional="$repo/tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-functional.ps1"
export OWNERSHIP_FUNCTIONAL="$functional"
pids=()
for index in {1..8}; do
    export OWNERSHIP_LAUNCH="$index"
    case $index in
        1|2|3) arguments=(-NoProfile -NonInteractive -File "$functional");;
        4|5) arguments=(-NoProfile -NonInteractive -Command '& $env:OWNERSHIP_FUNCTIONAL');;
        6|7) encoded=$(printf '%s' '& $env:OWNERSHIP_FUNCTIONAL' | iconv -f UTF-8 -t UTF-16LE | base64 -w0)
             arguments=(-NoProfile -NonInteractive -EncodedCommand "$encoded");;
        8) arguments=(-NoProfile -File "$functional");;
    esac
    pwsh "${arguments[@]}" > "$output/parent-$index.log" 2>&1 &
    pids+=("$!")
done
failed=0
for index in "${!pids[@]}"; do
    status=0
    wait "${pids[$index]}" || status=$?
    printf '%s\n' "$status" > "$output/parent-$((index+1)).exit"
    if [[ $status != 0 ]]; then failed=1; fi
done
[[ $failed == 0 ]] || { echo 'Functional cohort failed.' >&2; exit 1; }
sha256sum "$profile" > "$output/after.sha256"
cmp "$output/before.sha256" "$output/after.sha256"
mapfile -t profiles < <(find "$XDG_CACHE_HOME/powershell" -maxdepth 1 -name 'StartupProfileData*' -printf '%f\n')
[[ ${#profiles[@]} == 1 && ${profiles[0]} == StartupProfileData-NonInteractive ]]
find "$XDG_CACHE_HOME/powershell" -maxdepth 1 -type f -printf '%m %s %f\n' > "$output/files.txt"
echo 'OWNERSHIP_NATIVE_PASS: eight parents, one descendant, all three command shapes.'
