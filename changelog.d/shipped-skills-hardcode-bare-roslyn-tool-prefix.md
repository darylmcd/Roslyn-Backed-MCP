---
category: Fixed
---

- **Fixed:** `skills/mcp-server-surface-test/SKILL.md` and `skills/analyze/SKILL.md`'s Roslyn-MCP-connectivity hard-stops no longer gate on the literal bare tool prefix `mcp__roslyn__<suffix>` — they now recognize either that dev-build/self-hosted prefix or the marketplace-plugin's namespaced `mcp__plugin_roslyn-mcp_roslyn__<suffix>` form, and halt only when *neither* form resolves. A plugin-installed consumer's session no longer aborts a legitimate run just because its tool surface never contains the bare-prefixed literal. Advances `shipped-skills-hardcode-bare-roslyn-tool-prefix` (row stays open — the remaining shipped-skill sweep, the multisession-retro prompt file, and the genericity-guard regression assertion are deferred to a follow-on initiative).
