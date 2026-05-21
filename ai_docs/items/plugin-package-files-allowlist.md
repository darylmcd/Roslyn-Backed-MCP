# Plugin package files allowlist - design note

<!-- purpose: Decide how to reduce Claude Code plugin cache contents to the consumer surface. -->
<!-- scope: in-repo -->

**Initiative:** `plugin-package-files-allowlist`
**Date:** 2026-05-21
**Scope:** design-only; no manifest/package mutation in this row.
**Outcome:** no `plugin.json` allowlist field exists in the documented schema; implement a release-side packaging verifier plus a cache-sync allowlist in the repo's installer script.

## Mechanism Decision

The current manifest declares `mcpServers` and `skills` only. The Claude Code plugin docs describe the manifest as metadata plus component path fields; the component path list includes `skills`, `commands`, `agents`, `hooks`, `mcpServers`, `outputStyles`, `lspServers`, experimental themes/monitors, `userConfig`, `channels`, and `dependencies`, but no top-level package `files` allowlist. The reference also says unrecognized top-level fields are ignored with validation warnings rather than treated as packaging controls.

Docs checked:

- `https://code.claude.com/docs/en/plugins` - plugin directory structure and plugin-root component locations.
- `https://code.claude.com/docs/en/plugins-reference` - manifest schema, path behavior, and cache copy behavior.

Because there is no documented package-file allowlist field, adding a `files` field to `.claude-plugin/plugin.json` would be decorative and likely dangerous: it could pass validation as an ignored field while leaving the over-shipping bug intact.

## Consumer-needed File Set

Canonical consumer surface:

| Path | Why |
|---|---|
| `.claude-plugin/plugin.json` | Manifest and version. |
| `.claude-plugin/marketplace.json` | Local marketplace descriptor. |
| `.claude-plugin/mcp.json` | Plugin MCP server declaration. |
| `.claude-plugin/server.json` | Registry/metadata parity. |
| `skills/**` | Shipped skill prompts and helper scripts. |
| `hooks/hooks.json` | Shipped prompt-based hooks only. |
| `LICENSE` | Legal. |
| `README.md` | User-facing entrypoint. |
| `THIRD-PARTY-NOTICES.md` | Attribution. |
| `docs/setup.md`, `docs/reinstall.md`, `docs/compatibility.md` | Install/troubleshooting docs referenced from README. |
| `manifest.json` | Legacy manifest parity for non-Claude consumers. |

Explicitly excluded:

- `src/**`, `tests/**`, `samples/**`, `analyzers/**`
- `ai_docs/**`, `review-inbox/**`, `.claude/**`, `.github/**`, `.cursor/**`, `.vscode/**`
- `eng/**`, `changelog.d/**`, `.in_use/**`, `artifacts/**`
- Release/build internals such as `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, solution files, and Docker files.

## Verification Shape

Add `eng/verify-plugin-package-files.ps1` and call it from `eng/verify-release.ps1` after the existing skill-genericity check. The script should:

1. Build the allowlist from a checked-in data file, for example `.claude-plugin/package-allowlist.txt`.
2. Enumerate git-tracked files that would be copied by the current cache sync path.
3. Fail if any non-allowlisted file would ship.
4. Fail if any allowlisted required file is missing.
5. Print a compact diff of unexpected included files and missing required files.

This is intentionally release-side because the official plugin loader copies marketplace plugins into `~/.claude/plugins/cache` as versioned directories, and this repo's `eng/update-claude-plugin.ps1` currently mirrors that by copying git-tracked files from the marketplace clone.

## Implementation Row

`plugin-package-allowlist-enforcement` | Medium | none | Add a checked-in plugin package allowlist plus release/cache-sync enforcement so the Claude Code plugin cache contains only the consumer surface. Anchors: `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `.claude-plugin/package-allowlist.txt`, `eng/update-claude-plugin.ps1`, `eng/verify-release.ps1`, new `eng/verify-plugin-package-files.ps1`, `tests/RoslynMcp.Tests/PluginPackageAllowlistTests.cs`. Regression test shape: allowlist parser rejects repo-internal paths such as `ai_docs/`, `src/`, `tests/`, and `eng/`; verifier fixture fails on an unexpected file and passes on the canonical allowlist. Evidence: `ai_docs/items/plugin-package-files-allowlist.md`.
