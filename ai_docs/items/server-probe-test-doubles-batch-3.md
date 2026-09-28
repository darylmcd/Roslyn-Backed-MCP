# server-probe-test-doubles-batch-3 — Retire the private server-probe fakes in the update-check tests (batch 3 of 3)

**row:** `server-probe-test-doubles-batch-3` · **pri:** `Low` · **size:** `S` · **deps:** `server-probe-test-doubles-batch-1`

## Anchors

- `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs:22-258`
- `tests/RoslynMcp.Tests/ServerInfoUpdateWireContractTests.cs:73-197`

## Acceptance

- [ ] `ServerInfoUpdateLatestTests` deletes its private `FakeWorkspaceManager` and `FakeVersionProvider`. It uses the shared `FailClosedWorkspaceManagerStub` and the shared provider from `server-probe-test-doubles-batch-1`, passing each case's explicit status and last-checked time.
- [ ] `ServerInfoUpdateWireContractTests` replaces its private `MutableVersionProvider` with the shared provider's settable properties.
- [ ] Each converted call site passes the values its old fake produced. No assertion changes, and both classes keep their test count and pass.

## Evidence

- At `716b5e20`: `ServerInfoUpdateLatestTests.cs:22-30` (version provider with status and last-checked parameters) and `:65-86` (workspace manager), and `ServerInfoUpdateWireContractTests.cs:188-197` (mutable version provider). The full recount is in `server-probe-test-doubles-batch-1`.

## Context

- Batch 3 of 3. It depends on `server-probe-test-doubles-batch-1` for the shared provider.
- `server-info-update-available-next-major-nullable` (Defer) edits the same two test files. Sequence by hotspot if both are picked up.
- PR #1664 (open at filing) rewrites the `server_info` call sites in `ServerInfoUpdateLatestTests`. Re-derive the line numbers after it lands.
