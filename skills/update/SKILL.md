---
name: update
description: "Update the Roslyn MCP plugin. Use when: server_info shows an update is available, the user wants to update to the latest version, or the plugin reports an older version than NuGet. Handles both the global tool binary (Layer 1) and the Claude Code plugin metadata (Layer 2)."
user-invocable: true
argument-hint: ""
---

# Update Roslyn MCP Plugin

You are an update assistant. Your job is to update both layers of the Roslyn MCP plugin to the latest version.

## Background

The plugin has two layers that must be updated together:

| Layer | Provides | Update command |
|-------|----------|----------------|
| 1 — Global tool | The `roslynmcp` MCP server binary | `dotnet tool update -g Darylmcd.RoslynMcp` (or `dotnet tool install -g Darylmcd.RoslynMcp` if not installed) |
| 1b — Repo checkout (maintainers) | Same binary, from local `nupkg` | `just tool-update` (NuGet.org) or `just tool-install-local` after `just pack` |
| 2 — Claude Code plugin | Skills, hooks, marketplace metadata | `/plugin marketplace update` + `/plugin install` |

**Important:** The NuGet package ID is `Darylmcd.RoslynMcp` (NOT `RoslynMcp` — that is a different publisher's package).

## Server discovery

Call **`server_info`** on the running MCP host for semver + NuGet update hints. The full tool list lives in **`roslyn://server/catalog`**.

## Workflow

### Step 1: Check Current Version

Call `server_info` to get the current running version and check for updates. Report to the user:
- Current version (from `version` field, strip the `+hash` suffix)
- Latest NuGet version (from `update.latest` if available)
- Whether an update is available (from `update.updateAvailable`)

If `update` is `null`, the NuGet check hasn't completed yet. Tell the user the check is still pending and proceed to update anyway if they want the latest.

### Step 2: Update Layer 1 — Global Tool

**Preferred (any shell):**

```bash
dotnet tool update -g Darylmcd.RoslynMcp || dotnet tool install -g Darylmcd.RoslynMcp
```

**If the user is developing in this repository and has [just](https://github.com/casey/just):** run `just tool-update` (updates or installs, then lists global tools). To install the **locally built** package after `just pack`, use `just tool-install-local` (Windows ends `roslynmcp.exe` first to avoid file locks).

Report the result. If the tool reports "already up to date", note that and continue to Layer 2.

### Step 3: Update Layer 2 — Claude Code Plugin

Tell the user to run these two commands in the Claude Code chat input (they are slash commands handled by the Claude Code client, not by the agent):

```
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

**Note:** If the user's Claude Code client does not support `/plugin` slash commands (i.e., they get `/plugin isn't available in this environment`), tell them to update via their client's plugin/marketplace UI, or to uninstall and reinstall the plugin from the marketplace. Maintainers with the Roslyn-Backed-MCP source tree checked out have an agent-executable PowerShell fallback — see the repo-local override in `.claude/skills/update/` if present.

### Step 4: Report

Display a summary:
- Previous version
- New version (or "already up to date")
- Layers updated
- **Reminder: "Restart Claude Code to load the updated binary, skills, and hooks."**
