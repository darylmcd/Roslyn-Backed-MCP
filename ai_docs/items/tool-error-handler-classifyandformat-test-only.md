# tool-error-handler-classifyandformat-test-only — Remove or relocate test-only ClassifyAndFormat

**row:** `tool-error-handler-classifyandformat-test-only` · **pri:** `Low` · **size:** `M`


## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`

## Acceptance

- [ ] ClassifyAndFormat is removed or moved to test support; comments citing it corrected.
- [ ] Error-classification tests green.

## Evidence

wb: find_references shows no src callers; row structured-call-filter-owner-documentation-drift covers comments only. (doc-audit 2026-11-17)
