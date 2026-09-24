# mcpb-manifest-stale-or-retire — Retire or regenerate manifest.json

**row:** `mcpb-manifest-stale-or-retire` · **pri:** `Low` · **size:** `S`

## Anchors

- `manifest.json`

## Acceptance

- [ ] Either manifest.json is removed (and verify-version-drift / release-policy updated) or it sets tools_generated/prompts_generated true with a valid ${__dirname} command
- [ ] Decision recorded in docs/release-policy.md

## Evidence

- Checked field-by-field against MCPB MANIFEST.md v0.3 (no validator installed): required fields present; tools 8/174, no tools_generated; server.type binary entry_point 'RoslynMcp.Host.Stdio' not a bundled path; mcp_config.command 'roslynmcp' (global tool). Nothing packages it (docs/release-policy.md:75 'legacy DXT-style'); it ships inside the plugin payload. — see `ai_docs/audits/20260924-1305/report.md` (check C5) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Published repo (contract-care): the manifest is not read by Claude Code's loader, so removal is not a consumer-facing contract change, but confirm no other consumer parses it.
