# unused-code-analyzer-dead-field-complexity — Decompose dead-field analysis

**row:** `unused-code-analyzer-dead-field-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:691`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1070`
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs`

## Acceptance

- [ ] Separate project/tree traversal from field-reference classification and DTO projection.
- [ ] Reduce `FindDeadFieldsAsync` and `ClassifyDeadFieldReferenceLocation` below CC10.
- [ ] Preserve read, write, read-write, constructor-write, and removal-blocked diagnostics.

## Evidence

- Current-session touched-file complexity review measured CC16 and CC12 in the dead-field path.
