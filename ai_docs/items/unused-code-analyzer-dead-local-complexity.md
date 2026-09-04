# unused-code-analyzer-dead-local-complexity — Decompose dead-local analysis

**row:** `unused-code-analyzer-dead-local-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1213`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1293`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1328`
- `tests/RoslynMcp.Tests/DeadLocalDetectorTests.cs`

## Acceptance

- [ ] Separate local discovery, owner resolution, and exclusion policy into focused helpers.
- [ ] Reduce each listed method below CC10 without changing dead-local DTO order or confidence.
- [ ] Preserve captured, discarded, pattern, and framework-owned local exclusions.

## Evidence

- Current-session touched-file complexity review measured CC11–16 across the dead-local path.
