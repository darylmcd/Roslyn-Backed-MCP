# parameter-object-stale-apply-route — Fix nonexistent apply_refactoring route in parameter_object_preview

**row:** `parameter-object-stale-apply-route` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs:13` (class doc) and `:22` (tool `[Description]`) — both direct agents to `apply_refactoring`
- `tests/RoslynMcp.Tests/` (optional regression: catalog test asserting backtick-quoted tool names inside descriptions resolve to registered tools)

## Acceptance

- [ ] Description and class doc name only real redemption routes (`preview_multi_file_edit_apply` / `apply_composite_preview`)
- [ ] (Stretch, same PR if cheap) surface test cross-checks tool-name references in descriptions against the registered 173 names, preventing the next stale route

## Evidence

- `apply_refactoring` has zero matches anywhere in `src/` — a shipped tool description misdirects agents at the exact moment they try to redeem a preview — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §5
