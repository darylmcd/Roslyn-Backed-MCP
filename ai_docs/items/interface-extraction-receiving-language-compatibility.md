# interface-extraction-receiving-language-compatibility — Validate receiving language requirements

**row:** `interface-extraction-receiving-language-compatibility` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-parameter-semantics,interface-extraction-ref-return-contracts,interface-extraction-unsafe-signature-context,cross-project-receiving-namespace-compatibility,interface-extraction-nullable-context-projection`

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

## Evidence

- Cold source review at c5511d5c: ordinary signatures have no receiving language check before token storage; handler-specific probes use source parse options. receiving-probe-final.log proves exact C#13 collection-params emitted declaration compiles under C#13 and fails CS9202 under C#12 with the same references/options; source-owned signature-type support control gives the same language-only delta.
- Revision2 plan conditionally approved: compiler-check actual serialized declaration before all producer stores, preserve reference properties/aliases and same-assembly accessibility, compare complete internal diagnostic payload/multiplicity, and separately prove absolute nullable validity. Public refusals publish authored guidance plus bounded IDs only.
- Implementation remains pending the PR1840 actual hosted CS8632 discrepancy investigation. This row does not own assembly/reference closure, generic declarations or cross-project event emission.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
