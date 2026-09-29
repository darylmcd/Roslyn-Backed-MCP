# workspace-restore-public-failure — Explain explicit restore refusals safely

**row:** `workspace-restore-public-failure` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:640-680`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadRestoreFailureWireTests.cs`

## Acceptance

- [ ] Explicit `autoRestore:true` failures return a path-free public reason with category `InvalidOperation` instead of generic redaction.
- [ ] Wire test asserts the public message and `exceptionType` for the current exception marker, then a `Changed` fragment records any visible type change.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:664,676` throws plain `InvalidOperationException`, which the redaction layer collapses to generic text.

## Context

- Corrects the existing explicit path independently. The default-on slice depends on this distinction when it makes a failed automatic restore nonfatal.
