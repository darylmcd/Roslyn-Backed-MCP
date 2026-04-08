# Reinstalling the roslyn-mcp Claude Code plugin

There are **two layers** that need to be reinstalled when you change this
codebase, and they run in different places. Both layers are required — Layer 1
gives you the new server binary, Layer 2 gives Claude Code the new skills,
hooks, and marketplace metadata.

After both layers are updated, **restart Claude Code** so the new server
binary, skills, and hooks are loaded.

## Layer 1 — `roslynmcp` global .NET tool (the MCP server binary)

**Where to run:** any OS terminal (Git Bash, PowerShell, cmd) at the repo root
`C:\Code-Repo\Roslyn-Backed-MCP`. **Not** inside the Claude Code chat input.

**Command:**

```bash
dotnet publish src/RoslynMcp.Host.Stdio -c Release -p:ReinstallTool=true
```

This single command:

1. Restores and builds `RoslynMcp.Host.Stdio` in `Release` configuration.
2. Packs the NuGet package into `nupkg/Darylmcd.RoslynMcp.<version>.nupkg`.
3. Kills any running `roslynmcp.exe` processes.
4. Uninstalls the existing `roslynmcp` global tool (if any).
5. Reinstalls the freshly built tool from the local `nupkg` source.

After it finishes, `roslynmcp` on your `PATH` points at the new build.

> **Git Bash on Windows note:** the README shows `/p:ReinstallTool=true`. On
> Git Bash, the leading `/` is mangled into a path and MSBuild errors out with
> `MSB1008: Only one project can be specified.` Use `-p:ReinstallTool=true`
> instead. PowerShell and cmd accept either form.

## Layer 2 — Claude Code plugin (skills, hooks, marketplace metadata)

There are two ways to do this. **Pick the one that matches your Claude Code
client.**

### Option A — `eng/update-claude-plugin.ps1` (recommended; works everywhere)

**Where to run:** any PowerShell 7+ terminal at the repo root.

**Command:**

```powershell
pwsh ./eng/update-claude-plugin.ps1
```

The script performs the equivalent of `/plugin marketplace update` and
`/plugin install` by directly manipulating the same files those slash commands
write:

1. `git pull` in `~/.claude/plugins/marketplaces/roslyn-mcp-marketplace/` (the
   marketplace clone Claude Code reads from).
2. Wipes and re-syncs the plugin cache directory at
   `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/<version>/`,
   copying only git-tracked files from the marketplace clone.
3. Updates `lastUpdated` and `gitCommitSha` in
   `~/.claude/plugins/known_marketplaces.json` and
   `~/.claude/plugins/installed_plugins.json`.

Use this path if your Claude Code client does **not** intercept `/plugin` as a
built-in slash command. You can verify that's the case if typing
`/plugin marketplace update roslyn-mcp-marketplace` into the chat input
produces a normal LLM response (e.g. *"I don't recognize that command"*)
instead of being parsed and executed by the client.

> **Pre-requisite:** you must have installed the plugin at least once via a
> client that supports the slash commands (or by some other means) so the
> marketplace clone and metadata files exist. The script refuses to run on a
> machine that has never had the plugin installed.

### Option B — Slash commands (only if your Claude Code client supports them)

**Where to run:** inside the Claude Code REPL chat input box, the same place
you type prompts to the assistant.

**First-time install:**

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

**Update an already-installed plugin:**

```text
/plugin marketplace update roslyn-mcp-marketplace
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

What each command does:

- `/plugin marketplace update` re-fetches the marketplace's git repo
  (`darylmcd/Roslyn-Backed-MCP`) into
  `~/.claude/plugins/marketplaces/roslyn-mcp-marketplace/`.
- `/plugin install` re-clones the plugin contents into the cache directory
  and rewrites `~/.claude/plugins/installed_plugins.json` with the new commit
  SHA.

If your client returns an LLM response instead of executing the command, it
doesn't have the plugin slash-command parser. Use **Option A** instead.

## Full sequence after a code change

1. **Push your change to GitHub `main`.** Layer 2 only sees commits that are
   on `main` of `darylmcd/Roslyn-Backed-MCP` — local-only changes are invisible
   to the marketplace. Use `/ship` (or your normal PR + merge flow) first.
2. **Rebuild the global tool** in a terminal at the repo root:

   ```bash
   dotnet publish src/RoslynMcp.Host.Stdio -c Release -p:ReinstallTool=true
   ```

3. **Refresh the plugin** in PowerShell at the repo root:

   ```powershell
   pwsh ./eng/update-claude-plugin.ps1
   ```

   *(Or, if your Claude Code client supports slash commands, run them inside
   the REPL chat input instead — see Option B above.)*

4. **Restart Claude Code.**

## Why both layers are needed

| Layer | Provides | Updated by |
|---|---|---|
| 1 — global tool | The `roslynmcp` MCP server executable (C# tools, services, transports) | `dotnet publish ... -p:ReinstallTool=true` |
| 2 — Claude Code plugin | Skill definitions (`/roslyn-mcp:*`), pre/post-apply hooks, marketplace manifest | `eng/update-claude-plugin.ps1` (or `/plugin` slash commands) |

If you only do Layer 1, your skills and hooks stay on the old commit. If you
only do Layer 2, the slash commands launch the old `roslynmcp` binary. Always
do both after a substantive change.

## Troubleshooting

- **`MSB1008: Only one project can be specified.`** — You're on Git Bash and
  used `/p:ReinstallTool=true`. Switch to `-p:ReinstallTool=true`.
- **Slash commands like `/plugin install` come back as a normal chat reply.**
  Your Claude Code client doesn't intercept the `/plugin` slash command. Use
  `eng/update-claude-plugin.ps1` instead (Layer 2 → Option A).
- **`update-claude-plugin.ps1` fails with "Marketplace clone not found".**
  The plugin has never been installed on this machine. Install it once via a
  client that supports the slash commands, then use the script for updates.
- **`update-claude-plugin.ps1` runs but Claude Code still shows the old
  skills.** Restart Claude Code — skills and hooks are loaded at session
  start, not on the fly.
- **`/plugin install` reports the same version as before** (Option B). The
  marketplace cache is stale. Run `/plugin marketplace update
  roslyn-mcp-marketplace` first, then re-run `/plugin install`.
- **`roslynmcp` command not found after Layer 1.** Confirm
  `%USERPROFILE%\.dotnet\tools` is on your `PATH`. Reinstall with
  `dotnet tool install -g Darylmcd.RoslynMcp` if the publish step skipped install.
  (The unprefixed `RoslynMcp` package id is owned by another publisher on
  nuget.org; the CLI command name remains `roslynmcp`.)
- **Hooks block `*_apply` calls unexpectedly.** That's the pre-apply guard
  doing its job; you must call the matching `*_preview` first. This is
  documented in `README.md` § *Plugin Hooks*.

## Related docs

- [`README.md`](README.md) — primary install instructions and MCP client config
- [`docs/setup.md`](docs/setup.md) — packaging, Docker, CI artifacts
- [`ai_docs/runtime.md`](ai_docs/runtime.md) — runtime assumptions for AI
  sessions, including Roslyn MCP client policy
