---
name: update
installed_as: update
description: "Maintainer-local override of the shipped /roslyn-mcp:update skill. Refreshes both install layers from this checkout — the Layer 2 plugin cache with its release-pinned dnx server launch, and the Layer 1 global tool — then verifies both track the same version."
user-invocable: true
argument-hint: ""
---

# Update Roslyn MCP Plugin (maintainer override)

This `.claude/skills/update/` override is auto-discovered **only inside the Roslyn-Backed-MCP repo checkout** and takes precedence over the shipped `skills/update/SKILL.md` when present. It exists because shipped skills are scanned for repo-specific paths (`eng/...`) by `eng/verify-skills-are-generic.ps1` — so the shipped skill has to stay on the `/plugin` slash-command path, but maintainers in-repo have a PowerShell updater that works when the client refuses `/plugin`.

## Workflow

### Step 1: Check Current Version

Call `server_info`. Report current version, latest NuGet version, `updateAvailable`.

### Step 2: Update Claude Code Plugin

**Preferred (agent-executable, works even when `/plugin` slash-commands are unavailable):**

```bash
pwsh -NoProfile -File eng/update-claude-plugin.ps1
```

The script replicates `/plugin marketplace update` + `/plugin install` without going through the client:

- `git pull`s the marketplace clone at `~/.claude/plugins/marketplaces/roslyn-mcp-marketplace/`
- Re-syncs the plugin cache at `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/<new-version>/` from the git-tracked files matching `.claude-plugin/package-allowlist.txt` — read that file for the authoritative set. It admits consumer-facing files only (skills, `hooks/hooks.json`, the `.claude-plugin/*.json` manifests, `manifest.json`, LICENSE, README, notices, and a few `docs/*.md`), so the copied-file count is tens rather than hundreds. That is not a truncated copy: the server binary is fetched by the release-pinned `dnx` launch instead of being carried in the cache.
- Prunes stale `<old-version>/` cache directories
- Updates `installed_plugins.json` + `known_marketplaces.json` with the new version, commit SHA, and UTC timestamp

Requires the plugin to have been installed through Claude Code at least once (so the marketplace clone exists). Reports resolved version, target cache dir, copied-file count, and pruned stale dirs.

**Fallback (chat-side, if you prefer to go through the client):**

```
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

If the client responds with `/plugin isn't available in this environment`, use the PowerShell path above.

### Step 3: Update the Layer 1 global tool (required)

```bash
just tool-update
```

Resolves `Darylmcd.RoslynMcp` from NuGet.org (`dotnet tool update -g`, falling back to `install`). Use `just tool-install-local` after `just pack` instead when refreshing from an unpublished local build.

**This step is not optional.** At runtime the plugin launches its own release-pinned package through `dnx` and does not depend on this shim, which is why the step was once documented as optional — but "does not depend on" is not "does not drift." A stale Layer 1 means `roslynmcp` on the PATH, any standalone-client config pointing at it, and `dotnet tool list -g` all report an older server than the plugin runs. Both layers track the release.

If `dotnet tool update` reports the previous version right after a release, NuGet indexing has not caught up — wait and retry rather than accepting the old version.

**Windows file lock.** A running Layer 1 process holds its store directory, and the update can fail:

```
Failed to uninstall tool package 'darylmcd.roslynmcp':
Access to the path 'C:\Users\<user>\.dotnet\tools\.store\darylmcd.roslynmcp\<old>' is denied.
```

This is the common case, not an edge case — anyone who actually uses Layer 1 has one running, and it is very often **this session's own MCP server**: when Claude Code launched the server from the Layer 1 shim rather than the `dnx` pin, the holder's PID equals `server_info`'s `stdioPid` (verified on the v4.1.2 cut).

`just tool-update` now stops one owned process automatically before mutating anything. It runs `eng/stop-owned-tool-store-process.ps1` as a leading step, which reads `ROSLYNMCP_REINSTALL_PROCESS_ID` and `ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC` (the same two variables `just tool-install-local` uses), stops that one process only after confirming its image name is `roslynmcp` AND its image path resolves under the tool store root, and then asserts the store is unlocked — failing closed and naming every PID + image path still holding it (never terminating anything it cannot attribute) if one remains. Identify the holder by image path, never by process name alone (the plugin's `dnx`-launched Layer 2 server is also called `roslynmcp.exe`, runs from the NuGet package cache rather than the tool store, does not hold the lock, and killing it drops the MCP connection of whoever is attached):

```bash
pwsh -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='roslynmcp.exe'\" | Select-Object ProcessId, ExecutablePath, CreationDate"
```

Set the identity, then run the update:

```bash
$ownedPid = <owned-roslynmcp-pid>
$owned = Get-Process -Id $ownedPid
$env:ROSLYNMCP_REINSTALL_PROCESS_ID = $ownedPid
$env:ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC = $owned.StartTime.ToUniversalTime().ToString('O')
just tool-update
```

With no identity supplied, the leading step stops nothing and only asserts the store is unlocked — if a process still holds it, `just tool-update` fails closed with a clear "still locked by PID … " error instead of the opaque `dotnet` I/O failure above. Restarting Claude Code first also clears the lock on its own when the holder was this session's own server. The same shutdown/assert pair backs the local-pack path (`eng/reinstall-local-tool.ps1`, via the same two env vars or `-OwnedProcessId`/`-OwnedProcessStartedAtUtc`).

### Step 4: Verify both layers

```bash
pwsh -NoProfile -File eng/verify-install-layers.ps1
```

Reads Layer 1 from `dotnet tool list --global` and Layer 2 from the plugin cache (plus the cached `plugin.json`), and fails naming whichever layer is stale. Do not report the update complete until it exits 0.

### Step 5: Report

Same as the shipped skill — previous version, new version, **both layer versions with the verifier's result**, and a **reminder to restart Claude Code**.

## Why this override exists

During the v1.29.0 release-cut (PR #377), the Claude Code client refused `/plugin` slash-commands and the plugin cache remained stale. The repo already shipped `eng/update-claude-plugin.ps1` (maintainer-only, agent-executable, idempotent); this override makes that cache refresh the primary in-repo path.

The global-tool install is **not** part of the plugin's runtime contract — the plugin launches its own release-pinned package through `dnx`. It is still a required maintainer step (Step 3), because independence is not currency: after v4.1.2 the plugin cache was correct while `dotnet tool list -g` still reported 4.1.1. Runtime independence is exactly what let it rot unnoticed.
