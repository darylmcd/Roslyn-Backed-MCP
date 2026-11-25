# di-inference-lookalike-symbols — Correct collection and factory inference identity

**row:** `di-inference-lookalike-symbols` · **pri:** `Low` · **size:** `S` · **verified:** `0652f7fd82c457c3fd0530bce210544a1a225b5c`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:325` — TryRecordEnumerableElementType and BuildOverrideChains.
- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:594` — ResolveFactoryReturnedExpressionType.
- `tests/RoslynMcp.Tests/DiLifetimeOverrideTests.cs`

## Acceptance

- [ ] A consumed user-defined List<T> or IEnumerable<T> lookalike must not suppress real DI lifetime override chains; genuine framework collection consumers retain suppression. Add a public red-first regression for the collection-identity path.
- [ ] A real DI factory invoking an unrelated GetService<T>/GetRequiredService<T> helper that returns another valid implementation must not report T as that implementation; preserve genuine framework resolver inference. Add a public red-first regression for the factory-return path.

## Evidence

| Finding | Impact / reach / origin | Source-only call path |
|---|---|---|
| Collection identity | P2 / L2 / pre-existing | TryRecordEnumerableElementType:325-330 matches ConstructedFrom.Name; BuildOverrideChains:633-635 excludes the recorded service type. A constructor/property user collection with the same simple name can hide a genuine override chain. |
| Factory identity | P2 / L2 / pre-existing | ResolveFactoryReturnedExpressionType:594-600 returns TypeArguments[0] for any method with either resolver name; a helper whose actual return type is a different valid implementation is misattributed. |

- Re-derived at main 0652f7fd82c457c3fd0530bce210544a1a225b5c on 2026-11-25 with live Roslyn source reads and independent DI plan review. No runtime reproduction claimed.
- Distinct from registration admission in di-registration-framework-symbol-identity and default TryAdd projection in di-registrations-tryadd-omitted-claims-complete. Neither inference path is changed by the admission lane.

## Notes

- I5/V4: bundle Low findings from one review in the same production file. Two separable regression shapes; use the mandated per-checkbox commit/verdict discipline, not a fabricated shared production mechanism.
- Stable public value corrections require unchanged schemas/categories/published messages and before/after wire evidence under docs/release-policy.md. Do not suppress genuine chains or weaken validation.
