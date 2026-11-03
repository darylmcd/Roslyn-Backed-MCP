# publish-verifier-timeout-process-ownership — Drain killed verifier before fixture teardown

**row:** `publish-verifier-timeout-process-ownership` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/PublishVersionContextTests.cs` (publish provenance verifier process runner)

## Acceptance

- [ ] On timeout, terminate the owned verifier process tree, await bounded process exit, and drain both redirected output streams before test fixture teardown.
- [ ] Retain timeout as the primary failure and preserve any termination or drain failures as additional evidence.
- [ ] A red-first bounded verifier timeout regression proves the child is no longer live and redirected pipes are drained when the helper returns or throws.

## Evidence

- Cold review of draft PR #1953 at `232e325dc61cb907761837b51e0fcced89e03f08`: `PublishVersionContextTests` starts `stdoutTask` and `stderrTask`, but its timeout catch calls `process.Kill(entireProcessTree: true)` and throws without awaiting exit or either read. Fixture teardown can race a still-live child. This process runner predates the fixture cleanup change.
