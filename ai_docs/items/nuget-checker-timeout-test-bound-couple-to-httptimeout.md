# nuget-checker-timeout-test-bound-couple-to-httptimeout — couple the test wait bound to HttpTimeout instead of a literal

**row:** `nuget-checker-timeout-test-bound-couple-to-httptimeout` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs:122` (`await pending.WaitAsync(TimeSpan.FromSeconds(30))` — the hang-guard bound in `WaitForCompletionAsync`)
- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs:58` (`private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);`)

## Acceptance

- [ ] `WaitForCompletionAsync`'s hang-guard bound is derived mechanically from `NuGetVersionChecker.HttpTimeout` (e.g. a fixed multiple) rather than a standalone `30` literal, so the documented `>> HttpTimeout` invariant survives a future `HttpTimeout` change without a silent margin erosion.
- [ ] OR: if exposing `HttpTimeout` (currently `private static`) as `internal` + `[InternalsVisibleTo]` is judged not worth the production-visibility widening, close won't-fix with that rationale recorded — the existing load-bearing prose comment is then the accepted control.

## Evidence

- Row-4 code-quality finding (2026-06-20 top-n-remediation, `nuget-version-checker-timeout-test-wallclock-race`): the de-flake fix raised the bound to a `30 s` literal; its `>> HttpTimeout (3 s)` coupling is documented in a comment but cannot be enforced in code because `HttpTimeout` is `private static` (not test-visible). A future `HttpTimeout` bump silently erodes the margin with no compile-time link.

## Context

Deferred from row 4 (a test-only size-S row) because the structurally-tighter fix touches **production visibility** — out of that row's scope (Directive #6). The 30 s bound is ~10× the 3 s internal timeout, so the margin is robust today; this row is a hardening/maintainability follow-on, not a correctness gap.
