# test-early-return-silently-passes — Test branches silently pass

**row:** `test-early-return-silently-passes` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/AnalyzerShadowSweepPathAuthorityTests.cs:17`
- `tests/RoslynMcp.Tests/FileEditsDtoTests.cs:59`

## Acceptance

- [ ] The non-Windows branch of `Sweep_DoesNotFollowSharedOrWorkspaceLink` reports an explicit unsupported outcome; four DataRow cases cannot pass without running the path assertions.
- [ ] `Snapshot_LegacySynchronizationViewCannotMutateStoredEdits` checks the relevant behavior when SyncRoot is unsupported, and every path reaches a meaningful assertion.
- [ ] Audit other attributed test-method early returns before closing; retain legitimate local callback and post-assertion branches.

## Evidence

- Current source at origin/main 79a0899a: AnalyzerShadowSweepPathAuthorityTests.cs:17 returns before fixture setup on non-Windows. FileEditsDtoTests.cs:59 starts the SyncRoot branch; :63 catches NotSupportedException and returns before the method's final assertion.
- 2026-11-05 method-aware C# AST probe classified 82 if/return candidates: 67 explicit inconclusive guards, these 2 silent passes, 13 exclusions.
