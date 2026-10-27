# interface-extraction-unsafe-signature-context — Preserve unsafe signature context

**row:** `interface-extraction-unsafe-signature-context` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:83` (interface declaration)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:567` (interface modifiers)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve required unsafe context for emitted pointer/function-pointer signature types, including return/property/indexer and parameter positions across both extractors.
- [ ] Support representable output in unsafe-enabled receiving projects; refuse a concrete receiving-project policy incompatibility before token publication or writes without changing that policy implicitly.
- [ ] Prove red-first public producer compilation plus preview/apply/reload for supported cases, ordinary controls and unchanged token/workspace/disk state for refusals.

## Evidence

- PARKED with preserved drafts PR1840 and PR1854: dependency PR1854 atd8a70992 remains unqualified because Linux1 run40412863293 hit its30m deadline after2660passed/0failed/37skipped. The separate Windows owner-startup repair merged in PR1855 at49e0b03a1ed9d63c3f448a2bf545aeffc36c88bb; it does not establish a Linux cure. Integrate that prerequisite when this child resumes, before exact-head qualification. Live Docker/WSL provide7.75GiB; reviewed3GiB structural experiment stopped86 at memory cap before tests. No causally supported Linux correction or matching-capacity measurement is available in this session. Resume ci-linux-test-shard-budget with an adequate isolated Linux backend, corrected canonical Git remote and the reviewed fixed-source cohort; qualify the child before dependent code. Original Acceptance and full complex candidate tests stay unchanged; no weakened test or unchanged CI rerun.  See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generated interfaces carry only accessibility, so valid unsafe source signatures lose their compilation context. The shared parameter generator preserves the pointer syntax but does not repair the containing declaration. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by the third shard, PR1857). PR1840 closed as superseded by this decomposition; its combined candidate source and tests remain reference-only on branch `codex/interface-parameter-semantics` at `cb90656f`. Re-cut this row from current main; do not resume PR1840.

