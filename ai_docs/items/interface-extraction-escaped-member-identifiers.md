# interface-extraction-escaped-member-identifiers — Preserve escaped member identifiers

**row:** `interface-extraction-escaped-member-identifiers` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve C# identifier escaping for emitted method/property/event names in both extraction generators, covering every emitted member kind.
- [ ] Compile and bind keyword-named member declarations and calls through actual public extraction routes before and after apply/reload; preserve normal names and source trivia.
- [ ] Cover supported method type-parameter identifier spelling without expanding omitted generic declaration features owned by cross-project-interface-generic-type-parameters.

## Evidence

- PARKED with preserved drafts PR1840 and PR1854: dependency PR1854 atd8a70992 remains unqualified because Linux1 run40412863293 hit its30m deadline after2660passed/0failed/37skipped. The separate Windows owner-startup repair merged in PR1855 at49e0b03a1ed9d63c3f448a2bf545aeffc36c88bb; it does not establish a Linux cure. Integrate that prerequisite when this child resumes, before exact-head qualification. Live Docker/WSL provide7.75GiB; reviewed3GiB structural experiment stopped86 at memory cap before tests. No causally supported Linux correction or matching-capacity measurement is available in this session. Resume ci-linux-test-shard-budget with an adequate isolated Linux backend, corrected canonical Git remote and the reviewed fixed-source cohort; qualify the child before dependent code. Original Acceptance and full complex candidate tests stay unchanged; no weakened test or unchanged CI rerun.  See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found raw method.Name and event.Name passed to syntax factories. The new shared property/parameter projection escapes names; copied member emission must agree. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
