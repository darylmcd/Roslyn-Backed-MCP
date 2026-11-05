# apply-changes-operation-capture-duplication — First-ApplyChangesOperation capture is repeated with different no-op policies

**row:** `apply-changes-operation-capture-duplication` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs`

## Acceptance

- [ ] One capture helper with the no-op policy as an explicit parameter; behavior preserved.

## Evidence

- CodeActionService.cs ~171 and FixAllService.cs ~261.
