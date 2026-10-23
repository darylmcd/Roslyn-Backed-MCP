# gated-executor-default-drops-early-kill — Preserve early-kill semantics across executor implementations

**row:** `gated-executor-default-drops-early-kill` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IGatedCommandExecutor.cs:25-40`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `tests/RoslynMcp.Tests/Services/GatedCommandExecutorTests.cs`

## Acceptance

- [ ] An executor accepting nonempty early-kill patterns cannot silently execute through a fallback that discards them. Re-derive every implementation and caller before choosing the required contract.
- [ ] Observe causal regressions through the interface with a legacy-only executor and a streaming executor; retain literal argument handling, cancellation, timeout, process ownership and failure classification.
- [ ] Update all owned implementations and doubles together. Re-derive the published compatibility boundary; record an ADR and migration note for any breaking contract instead of retaining a default solely to avoid updating callers/tests.

## Evidence

- Source review on main0862736: the streaming overload's default body delegates to the legacy ExecuteAsync without forwarding earlyKillPatterns. Its summary explicitly says it ignores patterns so tests and legacy executors keep compiling.
- GatedCommandExecutor supplies a streaming implementation; this filing does not claim that production implementation drops patterns or that a child escaped its existing timeout. The defective mechanism is the interface fallback's silent loss of requested behavior. Dedicated runtime reproduction remains owed.

## Context

- Found while rechecking the preserved test-run package-isolation row. Independent of environment authority and the selected fifteen-item batch.

Live2026-10-15: forced companion of restore command safety migration. Remove the same pattern-dropping default in IDotnetCommandRunner as well as IGatedCommandExecutor; migrate every owned implementation/double and preserve actual argument/cancellation/timeout/ownership behavior. Require causal interface proof, not signatures alone.
