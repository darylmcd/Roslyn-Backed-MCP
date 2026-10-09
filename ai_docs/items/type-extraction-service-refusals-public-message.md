# type-extraction-service-refusals-public-message — Preserve safe extraction correction guidance

**row:** `type-extraction-service-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:34-54`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Probe every InvalidOperationException construction in the defining service and its same-file rewriter. Classify caller corrections versus internal invariants; publish caller corrections with the existing sanctioned type and safe, path/name-free guidance.
- [ ] Preserve exact error category, structured blocking-dependency contract and internal detail ownership. Do not mark raw interpolated caller names, paths or dependency summaries public.
- [ ] Red-first service-to-wire tests cover partial declarations, missing source/member selection and external-consumer refusal; malicious sentinels never appear in the complete envelope. Internal semantic failures remain generic.
- [ ] Pin verified ratchet baseline decreases after conversion; never expand the baseline. Keep existing compilation safety refusals effective.

## Evidence

- Immutable base `683dc0fcb503d02f49063e6909e327272ab4044c`: TypeExtractionService:34-54,93-99,238,266 uses plain BCL InvalidOperationException for caller errors and internal failures; caller details may include paths/names.
- ToolErrorHandler:156-178 publishes IPublicMessageException guidance, otherwise renders generic InvalidOperation correction. These plain expected refusals therefore lose actionable guidance; their raw messages must not simply be marked public.
- Existing argument-refusal dependency IDs address ArgumentException classification; the plain InvalidOperation producer remains inventoried by the landed construction ratchet. No runtime behavior proof is claimed by this source-only observation.

## Context

- One service-owned error-envelope regression shape. Follow all sibling constructions; widen complete scope for any forced companions.
- Separate from extract-type-preserve-untouched-trivia; if that implementation changes refusal semantics or construction identity, fix its introduced/exposed defect in the same PR.
