# workspace-execution-gate-not-found-message-sniffing — Classify stale-document errors with a typed marker, not an exception message substring

**row:** `workspace-execution-gate-not-found-message-sniffing` · **pri:** `Low` · **size:** `S` · **verified:** `df2f06c1dc495c3e0c4dfaf4ae7e67a54b044efa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`

## Acceptance

- [ ] `IsNotFoundError` (~:436-440) decides the stale-snapshot retry from a typed exception or marker, not by sniffing `ex.Message` for "Document not found"; every thrower of that condition is found (grep with `file:line`) and migrated.
- [ ] Red-first test: changing a thrower's message text does not change the retry decision; a plain unrelated InvalidOperationException containing the substring does not trigger a retry.

## Evidence

- Code-action preview planning review (2026-11-10): the gate's retry depends on message text, so any message-only correction of a "Document not found" refusal silently breaks it. `CrossProjectRefactoringService.cs:~41,~231` and `ClassSplitOrchestrator.cs:~36` throw the same plain message.

## Context

- Internal classification; no stable wire change when error categories and messages stay as they are.
