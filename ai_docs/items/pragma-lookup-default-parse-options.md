# pragma-lookup-default-parse-options — Pragma pair lookup must parse with the document's project parse options

**row:** `pragma-lookup-default-parse-options` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs`

## Acceptance

- [ ] SuppressionService.cs:~137, ~191, ~278 parse with the project parse options (preprocessor symbols); red-first test with a symbol-guarded region; these tools are stable, so qualify against release-policy Rule 1/ADR 0043 with wire evidence on both eras.

## Evidence

- Cold plan review of edit-syntax-check-project-parse-options: different mechanism, changes stable verify/widen DTO answers.
