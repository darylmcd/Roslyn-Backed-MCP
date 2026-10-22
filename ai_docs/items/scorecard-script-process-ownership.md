# scorecard-script-process-ownership — Own scorecard test process exit and output draining

**row:** `scorecard-script-process-ownership` · **pri:** `Low` · **size:** `S`

## Anchors

- tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs:533
- tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs:31

## Acceptance

- [ ] Replace RunPwshScript's synchronous parent wait and unbounded output GetResult calls with the shared asynchronous owned-process runner; update every caller without blocking bridges.
- [ ] Preserve all scorecard inputs, assertions, nonzero exits, output and the existing 60-second deadline.
- [ ] Retain process-tree termination and bounded exit/drain cleanup on timeout; exercise the inherited-pipe failure shape through the shared runner.

## Evidence

- 2026-10-17 output-drain investigation: helper lines 561-570 bounds only parent exit, does not await termination on timeout, and synchronously waits without a deadline on both redirected streams after exit. This is a separately observed defect, not established as the held verifier timeout's cause.
