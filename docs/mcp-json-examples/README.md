# `.mcp.json` examples

Drop one of these files into the **root of your repository** as `.mcp.json` to make the
`roslyn-mcp` server available to Claude Code / Cursor / any MCP client that honors
project-scope config.

| File | When to use it |
|------|----------------|
| [`minimal.mcp.json`](minimal.mcp.json) | You want defaults. Most repos. |
| [`with-overrides.mcp.json`](with-overrides.mcp.json) | You need to tune `ROSLYNMCP_*` values for this repo (slow builds, resource limits, etc.). |

## Why there's no `${user_config.*}` example

Earlier plugin releases shipped a `.mcp.json` that referenced
`${user_config.ROSLYNMCP_*}` placeholders expected to be substituted by Claude
Code's plugin enable-time prompt. That substitution throws a hard error on any
install flow that skips the prompt (automation, `bypassPermissions`,
pre-existing installs) — the MCP server never starts and no error surfaces to
the user. Fixed in v1.18.2 by dropping the `env` block from the plugin-shipped
files; the server now starts with compiled-in defaults and accepts literal
overrides via project-scope `.mcp.json` as shown here.

See `ai_docs/runtime.md` for the full list of `ROSLYNMCP_*` environment
variables and their defaults.
