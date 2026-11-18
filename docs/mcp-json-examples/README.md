# `.mcp.json` examples

Drop one of these files into the **root of your repository** as `.mcp.json` to make the
`roslyn-mcp` server available to Claude Code / Cursor / any MCP client that honors
project-scope config.

| File | When to use it |
|------|----------------|
| [`minimal.mcp.json`](minimal.mcp.json) | You want defaults and have the global tool installed. Most repos. |
| [`with-overrides.mcp.json`](with-overrides.mcp.json) | You need to tune `ROSLYNMCP_*` values for this repo (slow builds, resource limits, etc.). |
| [`dnx.mcp.json`](dnx.mcp.json) | Zero-install path — resolves the package from NuGet on each cold start. Requires .NET 10 SDK 10.0.100+ (`dnx` ships with the SDK). |

All examples set `ROSLYNMCP_SANCTIONED_ROOTS=.` so the launched server is authorized only for the
project working directory. Replace `.` with a platform-delimited root list when the client launches
the server from a different working directory or intentionally spans repositories.

## Why there's no `${user_config.*}` example

The plugin-shipped config uses no `${user_config.*}` placeholders, because that substitution fails hard on install flows that skip the enable-time prompt. It ships the literal `ROSLYNMCP_SANCTIONED_ROOTS` value `"."`.

Hand-written configs must set `ROSLYNMCP_SANCTIONED_ROOTS` themselves, since the boundary has no permissive default. History is in `CHANGELOG.md`.

See [`../setup.md`](../setup.md#environment-variables) for the full list of `ROSLYNMCP_*`
environment variables and their defaults.
