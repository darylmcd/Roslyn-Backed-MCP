# 06 — C5 Manifest

Rows (check C5):

- `mcpb-manifest-stale-or-retire` (Low) — manifest.json (MCPB 0.3) lists 8/174 tools and an unrunnable binary entry

Checked field by field against MCPB MANIFEST.md v0.3, fetched 2026-09-24 from modelcontextprotocol/mcpb; no validator was installed. Required fields are present. tools lists 8/174 without tools_generated, and there are no prompts or prompts_generated. The binary entry_point is not a bundled path, and mcp_config.command `roslynmcp` needs a global install instead of `${__dirname}`. Nothing packages the file (docs/release-policy.md:75), but it does ship in the plugin payload.
