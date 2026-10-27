# interface-extraction-receiving-language-compatibility — Validate receiving language requirements

**row:** `interface-extraction-receiving-language-compatibility` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-parameter-semantics,interface-extraction-ref-return-contracts,interface-extraction-unsafe-signature-context`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:538` (`CreateInterfaceCompilationUnit`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Use compiler-backed checks of actual generated declarations under receiving language options before any ordinary/composite preview token storage.
- [ ] Preserve representable ordinary and modern declarations; refuse demonstrated unsupported language forms without modifying LangVersion or other project policy.
- [ ] Cover collection params, scoped/ref-readonly and other emitted feature boundaries with valid-before controls, compatible receiving versions and ordinary-array controls through public producers.
- [ ] Refusals preserve token counts, solution/version, complete disk bytes and project options; publish authored guidance and diagnostic IDs without source/compiler message echoes.
- [ ] Do not substitute broad dependency refusal or hand-written feature-version tables for compiler checks; keep assembly/reference closure in its existing row.
- [ ] Parse the generated interface text twice: R under the receiving parse options and B under the source options. Cross-project: compile R against receiving references plus source references (union preserving alias/property views) plus the source compilation as a metadata reference, keeping the receiving project's own trees. Refuse when R has parse errors or when R's error multiset differs from B's; identity = id, severity, warning level, suppressed flag, invariant message, location kind/path/span/line span, additional locations and sorted properties.
- [ ] One compiler pass merged with the `InterfaceNullableProjection` legality check (`:183-186`), not a second validator; list every distinct id (remove `Take(5)` there too); never synthesize an id such as CS8632 — the compiler's own diagnostic under the project's real warnaserror policy decides.
- [ ] Matrix: refuse params collections at C# 12 (CS9202), `ref readonly` parameters at 11, `scoped` at 10, function pointers at 8 (unsafe on); accept params at 13, array params at 9 and 7.3, ref readonly at 12, scoped at 11, function pointers at 9; plus a project-reference alias and same-assembly internal accessibility.

## Evidence

- Unblocked 2026-10-27: PR1854 merged (`998b0e95`) and PR1857 moved hosted Linux to three shards; the earlier PARKED hold no longer applies.


- Cold source review at c5511d5c: ordinary signatures have no receiving language check before token storage; handler-specific probes use source parse options. receiving-probe-final.log proves exact C#13 collection-params emitted declaration compiles under C#13 and fails CS9202 under C#12 with the same references/options; source-owned signature-type support control gives the same language-only delta.
- Revision2 plan conditionally approved: compiler-check actual serialized declaration before all producer stores, preserve reference properties/aliases and same-assembly accessibility, compare complete internal diagnostic payload/multiplicity, and separately prove absolute nullable validity. Public refusals publish authored guidance plus bounded IDs only.
- The PR1840 hosted CS8632 discrepancy was fixture SourceRoot metadata, fixed by #1841. This row does not own assembly/reference closure, generic declarations or cross-project event emission.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by PR1857). PR1840 closed as superseded; its design for this row is inlined here and its archive branches were deleted (head still at GitHub `refs/pull/1840/head`). Re-cut from current main; do not resume PR1840.
