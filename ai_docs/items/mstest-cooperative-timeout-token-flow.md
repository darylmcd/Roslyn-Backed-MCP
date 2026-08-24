# mstest-cooperative-timeout-token-flow — Make cooperative timeouts cancel owned test work

**row:** `mstest-cooperative-timeout-token-flow` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallElicitationCoordinatorTests.cs`

## Acceptance

- [ ] Flow `TestContext.CancellationToken` through each operation guarded by `Timeout(..., CooperativeCancellation = true)`; do not rely on the attribute alone to stop work.
- [ ] On cancellation, bound and await every owned process, redirected stream, pending elicitation handler, and harness teardown path before fixture disposal.
- [ ] Preserve the notice verifier's parity assertions and the coordinator's success, cancellation, and slot-release contracts.
- [ ] One short-deadline regression matrix forces cancellation at both boundaries and proves each test returns without an owned process or pending handler.

## Evidence

Review on 2026-08-24 found both classes declare cooperative MSTest timeouts but never observe MSTest's token. A hung child verifier or elicitation harness can therefore outlive the advertised test ceiling and contaminate later tests. This is separate from the dependency-gate timeout hardening because it touches different process and in-memory ownership boundaries.
