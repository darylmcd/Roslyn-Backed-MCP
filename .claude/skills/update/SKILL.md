---
name: update
description: "Maintainer-local override of the shipped /roslyn-mcp:update skill. Use when: updating Layer 2 (Claude Code plugin cache) from this repo checkout and the user's client does not support /plugin slash-commands. Adds an agent-executable PowerShell fallback that the shipped generic skill cannot mention."
user-invocable: true
argument-hint: ""
---

# Update Roslyn MCP Plugin (maintainer override)

This `.claude/skills/update/` override is auto-discovered **only inside the Roslyn-Backed-MCP repo checkout** and takes precedence over the shipped `skills/update/SKILL.md` when present. It exists because shipped skills are scanned for repo-specific paths (`eng/...`) by `eng/verify-skills-are-generic.ps1` — so the shipped skill has to stay on the `/plugin` slash-command path, but maintainers in-repo have a PowerShell updater that works when the client refuses `/plugin`.

## Workflow (Layer 1 unchanged, Layer 2 replaced)

### Step 1: Check Current Version

Call `server_info`. Report current version, latest NuGet version, `updateAvailable`.

### Step 2: Update Layer 1 — Global Tool

```bash
dotnet tool update -g Darylmcd.RoslynMcp || dotnet tool install -g Darylmcd.RoslynMcp
```

In-repo alternatives: `just tool-update` (NuGet.org) or `just tool-install-local` after `just pack`.

### Step 3: Update Layer 2 — Claude Code Plugin

**Preferred (agent-executable, works even when `/plugin` slash-commands are unavailable):**

```bash
pwsh -NoProfile -File eng/update-claude-plugin.ps1
```

The script replicates `/plugin marketplace update` + `/plugin install` without going through the client:

- `git pull`s the marketplace clone at `~/.claude/plugins/marketplaces/roslyn-mcp-marketplace/`
- Re-syncs the plugin cache at `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/<new-version>/` from git-tracked files only (710+ files at 1.29.0)
- Prunes stale `<old-version>/` cache directories
- Updates `installed_plugins.json` + `known_marketplaces.json` with the new version, commit SHA, and UTC timestamp

Requires the plugin to have been installed through Claude Code at least once (so the marketplace clone exists). Reports resolved version, target cache dir, copied-file count, and pruned stale dirs.

**Fallback (chat-side, if you prefer to go through the client):**

```
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

If the client responds with `/plugin isn't available in this environment`, use the PowerShell path above.

### Step 4: Report

Same as the shipped skill — previous version, new version, layers updated, **reminder to restart Claude Code**.

## Why this override exists

During the v1.29.0 release-cut (PR #377), the Claude Code client refused `/plugin` slash-commands with `/plugin isn't available in this environment`. Layer 1 was updated via `just tool-install-local` but Layer 2 sat at 1.28.1 in the plugin cache. The repo already shipped `eng/update-claude-plugin.ps1` (a maintainer-only script, agent-executable, idempotent) that does exactly what the two slash-commands do — it just wasn't the documented primary path because the shipped skill isn't allowed to reference repo-specific `eng/` paths under `verify-skills-are-generic.ps1`. This override closes that gap for anyone running `/roslyn-mcp:update` from inside the Roslyn-Backed-MCP repo.
