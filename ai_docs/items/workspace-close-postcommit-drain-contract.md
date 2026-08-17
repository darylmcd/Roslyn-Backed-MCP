# workspace-close-postcommit-drain-contract — Define post-close process-drain cancellation

**row:** `workspace-close-postcommit-drain-contract` · **pri:** `Medium` · **size:** `S` · **deps:** `workspace-readiness-probe-error-redaction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — `CloseWorkspace` post-commit drain.
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] Treat workspace removal as the commit point and give optional process draining an explicit bounded cleanup token/timeout contract.
- [ ] Do not silently swallow caller cancellation as an ordinary drain failure after the close commits.
- [ ] Preserve the committed close result while emitting one secret-safe diagnostic for cleanup cancellation/failure.
- [ ] One table-driven drain outcome regression covers success, failure, caller cancellation, and timeout under the documented post-commit semantics.

## Evidence

- `CloseWorkspace` catches every process-drain exception after session removal; current tests intentionally convert cancellation into a debug-only successful result without a defined cleanup ownership model.
