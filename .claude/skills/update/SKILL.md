---
name: update
installed_as: update
description: "Maintainer-local override of the shipped /roslyn-mcp:update skill. Refreshes the complete plugin cache, including its release-pinned dnx server launch, from this checkout."
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
- Re-syncs the plugin cache at `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/<new-version>/` from the git-tracked files matching `.claude-plugin/package-allowlist.txt` (consumer-facing only: `skills/**`, `hooks/hooks.json`, the `.claude-plugin/*.json` manifests, `manifest.json`, LICENSE, README, notices, and three `docs/*.md` — 52 files at 4.1.0). A count in the dozens is correct, not a truncated copy; the server itself is fetched by the release-pinned `dnx` launch rather than carried in the cache.
- Prunes stale `<old-version>/` cache directories
- Updates `installed_plugins.json` + `known_marketplaces.json` with the new version, commit SHA, and UTC timestamp

Requires the plugin to have been installed through Claude Code at least once (so the marketplace clone exists). Reports resolved version, target cache dir, copied-file count, and pruned stale dirs.

**Fallback (chat-side, if you prefer to go through the client):**

```
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

If the client responds with `/plugin isn't available in this environment`, use the PowerShell path above.

### Step 3: Optional standalone global tool

Only when the maintainer also uses the separate global-tool install, run `just tool-update` (NuGet.org) or `just tool-install-local` after `just pack`. The plugin launches its own release-pinned package through `dnx` and does not depend on this shim.

### Step 4: Report

Same as the shipped skill — previous version, new version, optional global-tool result when requested, **reminder to restart Claude Code**.

## Why this override exists

During the v1.29.0 release-cut (PR #377), the Claude Code client refused `/plugin` slash-commands and the plugin cache remained stale. The repo already shipped `eng/update-claude-plugin.ps1` (maintainer-only, agent-executable, idempotent); this override makes that cache refresh the primary in-repo path. The separate global-tool install is optional and no longer part of the plugin contract.
