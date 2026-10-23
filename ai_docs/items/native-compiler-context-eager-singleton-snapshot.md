# native-compiler-context-eager-singleton-snapshot — native-compiler-context-eager-singleton-snapshot

**row:** `native-compiler-context-eager-singleton-snapshot` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilerReferenceContextGraph.cs`: `ValidateContexts`, `ContextSnapshot`.
- `tests/RoslynMcp.Tests/CompilerReferenceContextGraphTests.cs`: `DuplicateContexts_RequireCompleteEvaluatedConsistency`.

## Problem

Every fresh graph serializes each complete evaluated project XML and property map, although only repeated physical-path/global identities consume that snapshot. Single-project operations pay this cost on every freshness boundary.

## Acceptance

- Identify repeated exact context identities within one synchronous validation call.
- Retain the first occurrence's full snapshot at its original validation position for repeated groups; compare every duplicate to that snapshot.
- Preserve evidence and task-registration callbacks, cancellation, exact property/XML/import/evidence/registry equality and first-instance canonical mapping.
- Avoid unused singleton snapshots without caching graphs, bytes or authority across operations.
- Pass all 19 existing duplicate-context controls and unchanged native undo/fork deadline tests, then required full and hosted gates.

## Evidence

| Check | Result |
|---|---|
| Source derivation | Snapshot construction occurs for every new identity; its only consumer is the duplicate branch. |
| Candidate | Scratch duplicate-only eager-snapshot patch; no measured performance result yet. |
| Acceptance state | OPEN; do not claim deadline resolution from structural optimization. |
