# csproj-trivia-legacy-helper-lifecycle — Remove unused trivia restore lifecycle helpers

**row:** `csproj-trivia-legacy-helper-lifecycle` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:14-25`: class remarks describe obsolete snapshot/restore callers.
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:51`: containing class is internal; public method modifiers do not expose a public library type.
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:234`: SnapshotProjectsAsync.
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:300`: RestoreTriviaOnlyDriftAsync.
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:273`: live CreateSnapshot helper; retain it and its ProjectFileSnapshot record.
- `tests/RoslynMcp.Tests/CsprojReserializationTests.cs`: semantic/trivia regression coverage; rediscover exact live test anchors before changes.

## Evidence

| Read-only probe on 2026-10-17 | Result |
|---|---|
| `rg -n 'SnapshotProjectsAsync\|RestoreTriviaOnlyDriftAsync' . -g '*.cs' -g '!**/obj/**' -g '!**/bin/**'` | All five matches are declarations, XML docs, or logging text in CsprojSemanticEquality.cs; no product/test callsite. |
| Repository non-generated reference search | Historical CHANGELOG.md entry describes prior behavior; no live invocation identified. Preserve historical release notes. |
| `rg -n 'ProjectFileSnapshot\|ILogger\|Log' src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs` | ILogger/logging is used by the two unused lifecycle helpers. ProjectFileSnapshot remains used by live CreateSnapshot. |
| Backlog id/mechanism search | No existing matching legacy-helper lifecycle row found; preview trivia-nits is a different mechanism. |

## Acceptance

- [ ] Re-run declaration/callsite and reflection/string reference searches against the implementation head; confirm no active runtime caller before deleting either lifecycle helper.
- [ ] Remove only unused SnapshotProjectsAsync and RestoreTriviaOnlyDriftAsync, their exclusive logging import, and stale lifecycle comments.
- [ ] Preserve CreateSnapshot, ProjectFileSnapshot, XML semantic comparison, encoding/BOM behavior and all live consumers.
- [ ] Rewrite class documentation around the actual live helper contract; do not rewrite historical CHANGELOG.md entries.
- [ ] Preserve existing semantic/trivia tests and run applicable required validation; do not invent a test that asserts deleted methods merely remain absent.

## Scope

- Independent pre-existing dead code/stale documentation discovered during strict reader inventory.
- No product edits or cleanup authorized by this payload. File through the canonical backlog writer; do not advance the paused batch.
- This row does not replace the active held-reader acquisition fix or authorize changing supported aliased semantic reads.
