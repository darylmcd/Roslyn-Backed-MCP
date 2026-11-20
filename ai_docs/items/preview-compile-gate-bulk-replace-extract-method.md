# preview-compile-gate-bulk-replace-extract-method — Gate bulk_replace_type and extract_method previews on compilation

**row:** `preview-compile-gate-bulk-replace-extract-method` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/BulkRefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ExtractMethodTools.cs`

## Acceptance

- [ ] Red first: replacing parameter types with an interface that lacks members the consumers call is refused (or warned) at preview; today apply leaves 13 CS1061 errors.
- [ ] Red first: an extract_method selection starting mid-statement is refused or expanded with a warning; today apply introduces CS8604.
- [ ] Reuse the compile gate the type-extraction and interface previews already use; no second mechanism.

## Evidence

- P6-F1 (bulk_replace_type_apply, 13 errors) and P6-F9 (extract_method_apply, CS8604); both reverted cleanly with revert_last_apply.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
