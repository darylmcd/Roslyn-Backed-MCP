# change-signature-omitted-optional-binding — Preserve binding across omitted optional arguments

**row:** `change-signature-omitted-optional-binding` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Add and reorder preserve the original semantic parameter binding when positional calls omit optional arguments; materialize or name arguments where required, or provide a safe refusal for a genuinely unsupported shape.
- [ ] Regression tests compare Roslyn argument-to-parameter bindings before and after preview/apply, including inserted parameters beyond omitted optional slots and permutations moving supplied values across omitted slots.
- [ ] Preserve named/mixed-argument contracts and valid calls; do not replace semantic correctness with index clamping.

## Evidence

- Source checked at 89ccceb3c58f8dbfc02f856358f287535253c7d1: ChangeSignatureService.cs:204 contains `var clampedInsert = Math.Min(insertPosition, args.Count);` and appends the new expression at that index. For M(int a, int b=2), M(1), adding c at declaration end produces M(1,newC), which binds newC to b instead of c.
- Reorder at :394-402 uses `if (srcIdx < args.Count)` and appends only supplied arguments; dropping omitted slots can shift a supplied positional expression into a different parameter.
- The :198-203 comment incorrectly claims callsite append matches declaration append; index validity does not prove semantic binding preservation.

## Context

- Observed during cold diagnosis for change-signature-refusals-public-message. This is an algorithm/binding defect, distinct from public exception publication; no duplicate row found in the live backlog.
