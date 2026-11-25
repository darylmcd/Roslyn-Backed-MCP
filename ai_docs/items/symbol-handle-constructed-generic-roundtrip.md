# symbol-handle-constructed-generic-roundtrip — Preserve constructed named-type handle identity

**row:** `symbol-handle-constructed-generic-roundtrip` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:30-104`
- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:166-178`
- (new) `tests/RoslynMcp.Tests/ConstructedNamedTypeSymbolHandleTests.cs`
- (new) `tests/RoslynMcp.Tests/FindImplementationsAbstractTests.cs` — retained integration witness.

## Acceptance

- [ ] At SymbolHandleSerializer.cs:49-54 and :80-104, a constructed named-type symbol returned by real source/metadata binding must replay as the same construction and assembly-qualified type arguments, including nested substitutions; prove independent red-first identity tests and keep existing successful legacy, malformed/stale and member controls unchanged. Qualify actual raw tool requests/responses in both supported eras; no major-required contract change.

## Evidence

- Trigger -> harm: symbol_info at a valid Base<int> usage returns a handle that find_implementations cannot resolve, producing NotFound instead of navigation. Impact P2; reach L1; origin pre-existing.
- Base 16271513 (empty plan commit on 0652f7fd): literal 2025-11-25 and 2026-07-28 captures both refuse constructed-generic-source-handle with KeyNotFoundException. Dispatch head has seven passing cases and one failing handle assertion; no test normalization/removal authorized.
- Serializer stores constructed display text with declaration coordinates; replay resolves Base<T> then rejects the mismatched display. Existing payload omits argument assembly identity. Source independently re-derived on 2026-11-25; source hashes equal current main.
- Raw artifacts: ~/.codex/scratch/roslyn-remediate-20261125/abstract-implementations-implement/base-2025-11-25-raw.jsonl and base-2026-07-28-raw.jsonl; head-results/abstract-head.trx.

## Context

- I5/V4 finding and D5 prerequisite for find-implementations-abstract-members-empty. Preserve the frozen failing test and WIP in rem/abstract-implementations; do not route around the handle.
- D8/V3 decision is outstanding. No new identity payload, parser, optional compatibility shim, reflection into nonpublic SymbolKey, or original-definition fallback is approved. A bounded scratch study on pinned Roslyn 5.12 falsified the compiler reference-ID route: tuple labels were lost. No persisted-format implementation plan or minor qualification resulted. The earlier installed-server 5.9 API observations are not current pinned-library availability proof.
- Scope is named-type identity, not every symbol kind in the serializer. Type/member controls remain; do not invent a constructed-member defect without a witness.
- Existing lossy handles cannot reveal omitted assembly distinctions. Do not claim universal old-handle repair or narrow an existing successful guarantee silently. A correct option requiring a major bump is excluded from this run.

## Notes

Operator decision 2026-11-25: Park this lane; continue other rows. Preserve the frozen abstract-dispatch source and failing constructed-handle regression. Exact named-type identity design/prerequisite is deferred from this run; no new locator format, mechanism, narrowed identity guarantee, or major change is authorized. The scratch reference-ID candidate failed tuple preservation; no fallback was implemented.
