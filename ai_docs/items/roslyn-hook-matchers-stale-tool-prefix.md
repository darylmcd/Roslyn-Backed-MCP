# roslyn-hook-matchers-stale-tool-prefix — `hooks/hooks.json` matchers never fire under the plugin tool prefix

**row:** `roslyn-hook-matchers-stale-tool-prefix` · **pri:** `Medium` · **size:** `M`

## Anchors

- `hooks/hooks.json`
- `eng/verify-skills-on-edit.ps1:7`
- `.claude/settings.json`
- `ai_docs/bootstrap-read-tool-primer.md:146`

## Acceptance

- [ ] `hooks/hooks.json` matchers fire under the plugin-loaded tool prefix `mcp__plugin_roslyn-mcp_roslyn__*` as well as the bare `mcp__roslyn__*` form (or the file documents why bare-only is intended for shipped consumers).
- [ ] `.claude/settings.json`'s allowlist is consistent across both prefixes (today: 15 bare entries, 6 plugin entries).
- [ ] `eng/verify-skills-on-edit.ps1`'s header comment names its real wiring (`.claude/settings.json` PostToolUse), not `hooks/hooks.json`.
- [ ] `ai_docs/bootstrap-read-tool-primer.md:146,152` stops hardcoding `mcp__roslyn__*` and tells the reader to resolve the live prefix.
- [ ] A test asserts the matcher covers both prefixes, so a future prefix change fails loudly.

## Evidence

Both `hooks/hooks.json` PostToolUse matchers are literal-prefixed: `mcp__roslyn__server_info` and `mcp__roslyn__(rename_apply|extract_interface_apply|...)`. When the server is loaded as the Claude Code plugin — the default for this repo (`roslyn-mcp@roslyn-mcp-marketplace`) — live tool names are `mcp__plugin_roslyn-mcp_roslyn__*`, so neither matcher can fire. `.claude/settings.json` already carries both prefixes in its permission allowlist (15 bare entries, 6 plugin entries), confirming the drift is known but half-migrated.

Separately, `eng/verify-skills-on-edit.ps1:7` states "Hook config: hooks/hooks.json -> PostToolUse -> Edit|Write|MultiEdit". `hooks/hooks.json` contains no such entry; the script is wired from `.claude/settings.json` PostToolUse. A maintainer editing `hooks/hooks.json` to change this hook's behaviour would change nothing.

Note: `hooks/hooks.json` is a hard-blocked release-managed path (`eng/guard-release-managed-files.ps1`), so this work needs the release sentinel or a `/bump`-family route.
