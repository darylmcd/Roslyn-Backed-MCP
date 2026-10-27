# interface-extraction-ref-return-contracts — Preserve extracted ref returns

**row:** `interface-extraction-ref-return-contracts` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve ref and ref readonly return kinds on emitted methods, properties and indexers in both interface extractors.
- [ ] Verify generated symbol return kinds and real ref calls through extraction, inversion and extract-and-wire preview plus apply/reload; retain ordinary value-return controls.
- [ ] Observe the public regression fail on original production before the shared declaration projection fix.

## Evidence

- PARKED with preserved drafts PR1840 and PR1854: dependency PR1854 atd8a70992 remains unqualified because Linux1 run40412863293 hit its30m deadline after2660passed/0failed/37skipped. The separate Windows owner-startup repair merged in PR1855 at49e0b03a1ed9d63c3f448a2bf545aeffc36c88bb; it does not establish a Linux cure. Integrate that prerequisite when this child resumes, before exact-head qualification. Live Docker/WSL provide7.75GiB; reviewed3GiB structural experiment stopped86 at memory cap before tests. No causally supported Linux correction or matching-capacity measurement is available in this session. Resume ci-linux-test-shard-budget with an adequate isolated Linux backend, corrected canonical Git remote and the reviewed fixed-source cohort; qualify the child before dependent code. Original Acceptance and full complex candidate tests stay unchanged; no weakened test or unchanged CI rerun.  See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generators use ReturnType/Type alone, omitting ReturnsByRef/ReturnsByRefReadonly and property.RefKind. New indexer/property projection exposes the copied omission; repair in this cluster rather than defer it. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by the third shard, PR1857). PR1840 closed as superseded by this decomposition; its combined candidate source and tests remain reference-only on branch `codex/interface-parameter-semantics` at `cb90656f`. Re-cut this row from current main; do not resume PR1840.

