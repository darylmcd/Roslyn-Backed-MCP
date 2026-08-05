# workspace-eviction-retry-untested-branches — cover the non-recovering branches of the eviction auto-retry

**row:** `workspace-eviction-retry-untested-branches` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:257` (mid-call `WorkspaceEvictedException` branch)
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:304` (`catch (WorkspaceEvictedException) { throw; }`)
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:276` (`TryReclassifyAsEvicted`)
- `tests/RoslynMcp.Tests/WorkspaceEvictionAutoRetryTests.cs`

## Acceptance

- [ ] A test pins: reload throws → the original error envelope is preserved unchanged.
- [ ] A test pins: a typo'd/never-loaded `workspaceId` with an `IWorkspaceManager` wired → `TryReclassifyAsEvicted` returns null, no reload is attempted, and the NotFound envelope is unchanged (proves a bogus id never triggers an MSBuild reload).
- [ ] A test drives the mid-call `WorkspaceEvictedException` shape (evicted strictly between the gate precheck and the deeper service lookup) and asserts the hand-rolled `test_run` envelope matches the pre-existing inline `ReportStage(done)` → `ClassifyAndFormat` → `InjectSchemaHintIfPossible` output byte-for-byte.

## Evidence

- Code-quality review of PR #1141 (`workspace-eviction-no-auto-retry-on-tool-call`): all four shipped tests route through the same gate-precheck `KeyNotFoundException` shape; the `ex is WorkspaceEvictedException` branch at `ValidationTools.cs:257` and the rethrow at `ValidationTools.cs:304` are unreachable from any test in the diff, so the PR's own "byte-for-byte" envelope-equivalence claim for that path is unverified.

## Context

Spin-off from the `workspace-eviction-no-auto-retry-on-tool-call` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1141). No high-severity findings on this PR — this is coverage hardening, not a known bug.
