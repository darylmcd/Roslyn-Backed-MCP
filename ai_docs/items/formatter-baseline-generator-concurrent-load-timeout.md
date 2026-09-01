# formatter-baseline-generator-concurrent-load-timeout — Diagnose formatter contention

**row:** `formatter-baseline-generator-concurrent-load-timeout` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs:208`
- `eng/generate-format-baseline.ps1`

## Acceptance

- [ ] Distinguish a generator hang from host-wide MSBuild/dotnet-format contention with bounded operator-visible diagnostics.
- [ ] Prove the generator contract completes or fails with an actionable classification under concurrent repository build/test load.
- [ ] Preserve the five-minute safety bound unless repeated evidence justifies a measured replacement; do not hide contention by only increasing the timeout.

## Evidence

- A 2026-09-01 required `just ci` retry timed out the formatter baseline generator at five minutes while another repository testhost was active; the exact test passed in 49 seconds immediately after competing testhosts exited.

## Context

Treat this as test-infrastructure reliability work. Keep generated baseline semantics and deterministic inventory assertions unchanged.
