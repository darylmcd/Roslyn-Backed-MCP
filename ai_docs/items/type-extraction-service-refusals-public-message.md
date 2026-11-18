# type-extraction-service-refusals-public-message — Preserve safe extraction correction guidance

**row:** `type-extraction-service-refusals-public-message` · **pri:** `Defer` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:34-54`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Reopen at the next operator-approved major release. Probe each InvalidOperationException construction in the service and its same-file rewriter. Publish only actionable caller corrections with the sanctioned public exception type and safe, path-free guidance.
- [ ] Preserve the exact error category, structured `blockingDependencies` field and its member names, and internal detail ownership. Keep raw caller names, paths and dependency summaries out of the public `message`.
- [ ] Reproduce and verify the refusal-message migration on the raw wire in both supported protocol eras. Cover partial declarations, missing source/member selection and external-consumer refusals. Assert the full envelope exposes caller member names only through intended structured fields. Keep internal semantic failures generic.
- [ ] Add the ADR and migration note for the message change. Pin verified construction-ratchet decreases and retain existing compilation safety refusals.

## Evidence

- Immutable base `683dc0fcb503d02f49063e6909e327272ab4044c`: TypeExtractionService:34-54,93-99,238,266 uses plain BCL InvalidOperationException for caller errors and internal failures; caller details may include paths/names.
- ToolErrorHandler:156-178 publishes IPublicMessageException guidance, otherwise renders generic InvalidOperation correction. These plain expected refusals therefore lose actionable guidance; their raw messages must not simply be marked public.
- Existing argument-refusal dependency IDs address ArgumentException classification; the plain InvalidOperation producer remains inventoried by the landed construction ratchet. No runtime behavior proof is claimed by this source-only observation.

## Context

- One service-owned error-envelope regression shape. Follow all sibling constructions; widen complete scope for any forced companions.
- Separate from extract-type-preserve-untouched-trivia; if that implementation changes refusal semantics or construction identity, fix its introduced/exposed defect in the same PR.

## Notes

Operator decision 2026-11-09: defer the stable extract_type_preview public-message change to the next major release. Do not implement this row in 5.x. Preserve blockingDependencies member names as intentional structured caller data; they do not justify a major bump on their own. At the major release, provide ADR, migration note and raw-wire coverage for the public message change. Read the complete-envelope sentinel acceptance as excluding intentional blockingDependencies names; check that raw paths, names and exception details do not leak through the public message or other unintended envelope fields.

2026-11-11 5.x screen at 52f2c418: the live catalog marks extract_type_preview stable. ToolErrorHandler.cs:667 still passes BlockingDependencies as structured caller data. Keep member names in blockingDependencies; the next approved major trigger is the public-message migration, with ADR, release note and both raw protocol eras.

5.x screen at a12412bb: extract_type_preview is stable, and ToolErrorHandler.cs still emits BlockingDependencies as the structured blockingDependencies field. Keep member names there. Reopen refusal-message publication only at the next major with an ADR, migration note and both supported raw protocol eras.

2026-11-14 5.x screen at ddf4cc6d. Stable extract_type_preview still emits blockingDependencies through ToolErrorHandler.cs:665–667. Preserve structured member names. Reopen public refusal-message changes at the next operator-approved major with ADR, migration note and both raw protocol eras.

2026-11-14 operator constraint reaffirmed on 5.1.1: hold the published extract_type_preview refusal-message change until the next operator-approved major with ADR, migration note and both raw eras. Preserve blockingDependencies member names as structured caller data. The retention is 5.x contract-preserving.

2026-11-17 screen at c705e2e1. Reopen the public-message migration at the next operator-approved major, with an ADR, migration note and both raw protocol eras. The stable tool catalog still lists extract_type_preview, and ToolErrorHandler.cs:665–667 emits blockingDependencies as structured caller data. Preserve member names in that field. The major trigger is the change to public refusal prose.
