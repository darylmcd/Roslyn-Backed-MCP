# test-run-failures-pagination-truncation — re-home test_run failures pagination/truncation as its own reviewed unit

**row:** `test-run-failures-pagination-truncation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` (`RunTests` / `RunTestsOnceAsync`)
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs` (`ParseTestRun`, per-failure `Message`/`StackTrace`)

## Acceptance

- [ ] Implement `failuresOffset`/`failuresLimit` pagination on `test_run`'s `failures` array (default limit 25, `failuresTotal`/`hasMoreFailures` metadata) mirroring `test_discover`'s existing offset/limit/hasMore pattern; aggregate `Total`/`Passed`/`Failed`/`Skipped` counts must never be truncated
- [ ] Implement head-truncation of each `TestFailureDto.Message` (500 chars) / `StackTrace` (1500 chars) in `DotnetOutputParser`, keeping the head (assertion text / throw-site frame) not the tail, with a visible `"... [truncated]"` marker — mirrors `DotnetCommandRunner`'s existing tail-truncation of `StdOut`/`StdErr` at 12000 chars, but head-oriented since the start of a failure message/stack is what's diagnostically useful
- [ ] **Before claiming this guards a live hazard**, find the actual MCP transport/output cap constant/limit that applies to this server's responses (a repo-wide grep at implementation time — `grep -rn "MaximumResponseSize\|MaxResponseSize\|ResponseSizeLimit\|OutputSizeLimit"` across `src/` and the pinned `ModelContextProtocol.Core` SDK sources — turned up NOTHING in-repo as of 2026-08-11; the cap, if any, is likely enforced by the MCP client/transport layer, not this server, so the implementer must track down where it actually lives before the guard's necessity can be asserted rather than assumed) and measure a real large-N `test_run` response's serialized size against it
- [ ] Regression: a synthetic large-N (e.g. 400) failure-count fixture proving the paginated response stays well under the measured cap; a TRX-fixture test proving per-failure truncation keeps the head; an offset/limit boundary test

## Evidence

This was fully implemented once already, in commit `84d05497` on branch `row/test-run-unfiltered-bare-error-rootcause`, as the row's *first-pass* fix — before a spec-compliance re-review determined the row's actual confirmed root cause was a DIFFERENT mechanism (a `WorkspaceExecutionGate` internal-timeout `OperationCanceledException` escaping unclassified past the MCP SDK's top-level catch-all — see `test-run-unfiltered-bare-error-rootcause`, now fixed there). The pagination/truncation diff was found correct in isolation (`TestRunFailureEnvelopeTests` passed, 57/57 in the combined run) but was reverted from that row in a later commit on the same branch because:
1. The row's acceptance criteria was an either/or ("fix hypothesis (a) OR hypothesis (b), per the determined cause") and the confirmed cause was (b), not (a) — shipping (a)'s fix anyway was scope creep authorized only by acceptance-bullet text the implementer itself rewrote in the same diff.
2. The diff's own comments admit hypothesis (a) — payload overflow — "was never confirmed... no repro was ever measured against the actual MCP output cap." On a published server, an unrequested feature (two new public MCP tool params) plus a silently lossy default behavior change (head-truncating every failure's Message/StackTrace) needs its own scoped review, not a ride-along on an unrelated root-cause fix.

The reverted diff is a legitimate, valuable piece of defensive hardening (dotnet test CAN genuinely produce a multi-hundred-KB-to-MB response with hundreds of real failures) and is a reasonable starting point for this row — see `git show 84d05497 -- src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs` in this repo's history for the full patch content (params, pagination logic, response-shape change, truncation constants, and the three regression tests) — but re-implement it as its OWN reviewed unit, with the output-cap measurement gap closed first.

## Context

Grep used to confirm no in-repo output-cap constant exists as of 2026-08-11: `grep -rn "MaximumResponseSize|MaxResponseSize|ResponseSizeLimit|OutputSizeLimit|MAX_OUTPUT" src/` — zero hits. `DotnetCommandRunner`'s existing 12000-char StdOut/StdErr tail-truncation is a precedent for *some* server-side size discipline existing, but its own justification (if any) should be checked too rather than assumed to derive from the same cap.
