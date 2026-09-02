# tool-update-owned-process-shutdown — owned-process shutdown for the NuGet global-tool update

**row:** `tool-update-owned-process-shutdown` · **pri:** `Medium` · **size:** `M`

## Anchors

- `justfile:106`
- `eng/reinstall-local-tool.ps1:131`

## Acceptance

- [ ] `just tool-update` completes while an owned Layer 1 `roslynmcp` process is running, using the same PID + start-time ownership guard as the local-pack path rather than a new killer.
- [ ] The Layer 2 `dnx`-launched server is never a termination candidate; selection is by image path under the tool store, not by the `roslynmcp.exe` image name.
- [ ] A lock that cannot be attributed to an owned process fails closed with the holding PID named, rather than terminating anything.
- [ ] Regression test covers: owned process running -> update succeeds; unowned holder -> fail-closed with the PID reported.

## Evidence

- 2026-09-02, immediately after making the Layer 1 refresh mandatory: `just tool-update` failed with `Failed to uninstall tool package 'darylmcd.roslynmcp': Access to the path 'C:\Users\daryl\.dotnet\tools\.store\darylmcd.roslynmcp\4.1.1' is denied.` The holder was PID 11088, `C:\Users\daryl\.dotnet\tools\roslynmcp.exe`.
- `eng/reinstall-local-tool.ps1` already solves this for the local-pack route (`ROSLYNMCP_REINSTALL_PROCESS_ID` + `ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC`, shipped in 4.1.2 as `local-tool-reinstall-process-ownership`), but its `-PackageSource` is validated as a local directory (`Resolve-Path` + `Test-Path -PathType Container`), so the NuGet route cannot reuse it as written.

## Context

`/release-cut` Step 6b now requires the Layer 1 refresh, so a step that fails whenever the tool is in use is a real gap, not a nuisance — anyone who actually uses Layer 1 has a process holding the store. The interim contract is documented in `.claude/skills/update/SKILL.md` Step 3: identify the holder by image path, stop only an owned instance, retry.

Do not extend the killer to match on image name. The plugin's `dnx`-launched Layer 2 server is also `roslynmcp.exe`, runs from the NuGet package cache, does not hold the tool-store lock, and terminating it drops a live MCP connection.

## Amendment — 2026-09-02 (cold plan-deepener; verified against live source, no code shipped)

Row is **shovel-ready** — both anchors verified live.

**Root cause.** `justfile:106-108` runs `dotnet tool update -g Darylmcd.RoslynMcp || dotnet tool install -g …` with no lock handling at all. The ownership guard that solves this exists but is unreachable from that route: `eng/reinstall-local-tool.ps1:33-99` (`Stop-OwnedRoslynMcpProcess`, env fallback `:24-31`) is a script-scope function **closing over the script's own `param()` variables** rather than taking parameters, and the script is hard-bound to the local-pack pipeline — `:131-134` rejects any non-directory `-PackageSource`, `:135-149` derives the version from `-ProjectPath` via `dotnet msbuild -getProperty:Version`, `:167-181` does uninstall-then-`install --add-source`. The guard is also blind on the axis this row cares about: `:69` attributes a process by `ProcessName -eq 'roslynmcp'` only, which cannot distinguish the Layer 1 shim from the plugin's `dnx`-launched Layer 2 server of the same image name.

**DESIGN DECISION (resolved) — extract the guard into a shared script; do NOT teach `-PackageSource` to accept a NuGet source.** The code decides it: `:131-134`, `:135-149` and `:167-181` are three independent local-pack-only stages the update route needs none of (it is a single `dotnet tool update -g`). Making `-PackageSource` polymorphic would branch all three and leave a `Mandatory` parameter the new mode ignores. The genuinely shared asset is the ~70-line ownership guard, not the install pipeline.

**Approach.**
1. New `eng/stop-owned-tool-store-process.ps1` exposing `Stop-OwnedToolStoreProcess -OwnedProcessId -OwnedProcessStartedAtUtc -ToolStoreRoot` (body lifted from `reinstall-local-tool.ps1:33-99` with closed-over params made explicit; the `$PID` self-kill refusal `:57-59`, round-trip timestamp parse `:47-55`, 1s tolerance and 10s `WaitForExit` all preserved) plus a new **image-path-under-tool-store** check; and `Assert-ToolStoreUnlocked -ToolStoreRoot`, which enumerates `Get-CimInstance Win32_Process`, selects by `ExecutablePath` prefix under the resolved store, and `throw`s naming every holding PID and path if a holder survives — fail closed, terminate nothing.
2. `eng/reinstall-local-tool.ps1` dot-sources it and deletes the inlined function; the env-var fallback `:24-31` stays in the caller so the published `ROSLYNMCP_REINSTALL_PROCESS_ID` / `…_STARTED_AT_UTC` contract is untouched.
3. `justfile:106-108` gains a leading `pwsh -NoProfile -File ./eng/stop-owned-tool-store-process.ps1` before `dotnet tool update`. A `throw` returns non-zero under both shells configured at `justfile:10-11`, so `just` aborts before the destructive step.
4. `.claude/skills/update/SKILL.md:57-72` replaces the interim manual contract with the automated one.

**Scope — exactly at the Rule 3 base cap of 4:** `eng/stop-owned-tool-store-process.ps1` (new), `eng/reinstall-local-tool.ps1`, `justfile`, `.claude/skills/update/SKILL.md`. Tests 2: `LocalToolReinstallProcessOwnershipTests.cs` extended (thread `-ToolStoreRoot` through the fixture wrapper `:142-183`; extend static contract assertions `:102-112`), `ToolUpdateOwnedProcessShutdownTests.cs` (new).

**Executor traps (all load-bearing):**
- **Keep** the existing `ProcessName -eq 'roslynmcp'` check (`:69`) as a retained sanity gate *in addition to* the new image-path gate. Removing it makes `docs/reinstall.md:52` stale, which pulls in a 5th production file and forces a split.
- `LocalToolReinstallProcessOwnershipTests.cs:111` bans the literal `Get-Process -Name` in the script text — discovery must use `Get-CimInstance Win32_Process` (which `.claude/skills/update/SKILL.md:69` already documents). `Get-Process -Id` on the explicit-PID path is unaffected.
- `InstallLayerRefreshContractTests.cs:46` asserts the literal `just tool-update` survives in the skill text.
- `StartNamedRoslynMcpProcess` (`:115-140`) copies `PING.EXE` to `<fixtureRoot>/<name>/roslynmcp.exe`, i.e. outside any real tool store — the existing green test only stays green once `-ToolStoreRoot` is threaded to the fixture root. That is why the parameter exists.

**Scheduling:** `justfile` is shared with `ci-router-pure-decision` (different recipe — `ci:` at `:79` vs `tool-update` at `:106`). File-overlap edge, not a build-order dependency; do not co-schedule in one parallel wave.

**Bad code observed, tracked separately:** (a) `eng/reinstall-local-tool.ps1:33-99` silently closes over script-scope `param()` variables instead of taking parameters — the implicit coupling that made the guard unreusable, i.e. the root cause this row pays for (fixed by this row); (b) `eng/reinstall-local-tool.ps1:123` folds an operational remedy ("supply the PID and round-trip UTC start time…") into a generic `Invoke-DotnetStep` failure message that fires for *every* dotnet step, including version discovery, where the advice is nonsense — row `reinstall-local-tool-generic-step-error-misadvises`.
