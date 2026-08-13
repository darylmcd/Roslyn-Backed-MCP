---
category: Fixed
---

- **Fixed:** The multi-session retro prompt's session-relevance filter (`ai_docs/prompts/roslyn-mcp-multisession-retro.md`) now matches Roslyn MCP tool calls under any client-assigned prefix — the regex `mcp__\S*roslyn\S*__\S+` covers the bare dev-build form, the marketplace-plugin form `mcp__plugin_roslyn-mcp_roslyn__…`, and any other registration key — instead of only the bare `mcp__roslyn__` literal, the defect that made earlier retros silently undercount cross-repo usage by dropping whole plugin-namespaced repos from the sample. Its §2a **Tool** column now asks for the tool name including whichever prefix the session used. The `refactor` and `refactor-loop` skills' zero-semantic-calls advisory and the `mcp-server-surface-test` README's reachability description are likewise prefix-agnostic, mirroring the resolve-then-pin language already shipped in that bundle's `SKILL.md`. Part of an ongoing sweep; the hard-gate precheck blocks are tracked separately.
