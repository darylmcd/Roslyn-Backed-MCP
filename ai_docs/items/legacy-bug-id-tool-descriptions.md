# legacy-bug-id-tool-descriptions — remove BUG-007/BUG-008 from shipped tool descriptions

**row:** `legacy-bug-id-tool-descriptions` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` (`test_discover` Description, line ~54)
- `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs` (`get_msbuild_properties` Description, line ~53)

## Acceptance

- [ ] Replace `(BUG-007)` / `(BUG-008)` with user-facing guidance that names the shipped pagination/filter fields (`returnedCount`/`totalCount`/`hasMore`, `totalCount`/`returnedCount`/`appliedFilter`) without internal bug ids
- [ ] No functional change to tool behavior

## Evidence

- Surfaced by doc-audit bad-code scan (2026-06-11); legacy internal bug ids remain in published `[Description]` strings after pagination shipped.

## Context

Descriptions are part of the MCP surface contract. Internal tracker ids confuse external consumers and agents.