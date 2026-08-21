---
name: update
installed_as: roslyn-mcp:update
description: "Update the Roslyn MCP Claude Code plugin and its release-pinned server launch. Also explains the separate optional global-tool update path."
user-invocable: true
argument-hint: ""
---

# Update Roslyn MCP Plugin

You are an update assistant. Your job is to update the Claude Code plugin to the latest version.

## Background

The plugin launch is self-contained; a global tool is a separate install option:

| Layer | Provides | Update command |
|-------|----------|----------------|
| Claude Code plugin | Release-pinned `dnx` server launch, skills, hooks, marketplace metadata | `/plugin marketplace update` + `/plugin install` |
| Optional global tool | Standalone `roslynmcp` command for non-plugin clients | `dotnet tool update -g Darylmcd.RoslynMcp` |

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

### Step 2: Update Claude Code Plugin

Tell the user to run these two commands in the Claude Code chat input (they are slash commands handled by the Claude Code client, not by the agent):

```
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

**Note:** If the user's Claude Code client does not support `/plugin` slash commands (i.e., they get `/plugin isn't available in this environment`), tell them to update via their client's plugin/marketplace UI, or to uninstall and reinstall the plugin from the marketplace. Maintainers with the Roslyn-Backed-MCP source tree checked out have an agent-executable PowerShell fallback — see the repo-local override in `.claude/skills/update/` if present.

### Step 3: Optional Global Tool

Only when the user also uses the standalone global-tool install, run `dotnet tool update -g Darylmcd.RoslynMcp`. The plugin itself does not require that shim.

### Step 4: Report

Display a summary:
- Previous version
- New version (or "already up to date")
- Plugin version updated
- Optional global-tool result, when requested
- **Reminder: "Restart Claude Code to load the updated server pin, skills, and hooks."**
