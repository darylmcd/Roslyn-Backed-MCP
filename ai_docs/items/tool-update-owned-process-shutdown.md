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
