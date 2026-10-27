# relocation-receiving-namespace — Select supported namespaces for relocating new files

**row:** `relocation-receiving-namespace` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-receiving-namespace-compatibility`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs` (ApplyNamespaceRewriteAsync / BuildMovedTypeCompilationUnit)
- `src/RoslynMcp.Roslyn/Helpers/ReceivingNamespaceFactory.cs` (shared required-options factory delivered by first consumer)
- `tests/RoslynMcp.Tests/RelocationReceivingNamespaceIntegrationTests.cs` (new native public-route regression)
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs` (existing affected consumer)

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (existing raw framing consumer if shared framing extraction is needed)

## Acceptance

- [ ] Reuse the shared receiving-options factory in this owner, passing actual sourceDocument.Project.ParseOptions through every affected private caller with required arguments. No source/latest guess, optional compatibility shim, duplicated feature threshold or compiler-policy mutation.
- [ ] Observe valid-before native C#7.3/C#9 source fail specifically for unsupported emitted namespace syntax, then green serialized preview and actual applied/reloaded compilation, symbol binding and runtime. Retain C#10 boundary/C#14 modern controls and supported global output.
- [ ] Preserve preview nonmutation, project/source disk bytes before apply, actual receiving options, token/version/authorization invariants and unrelated bindings. Reuse A moved-file ownership helper and its qualified header/documentation/directive/import/EOF behavior. Reuse or extract raw boundary framing rather than copy the CrossProject framing branch.
- [ ] Cover all affected consumers, compile_check, formatter with zero new debt, existing/new native class cohorts and exception-construction audit. Own fragment, independent cold review, exact-head scoped gate and green hosted checks precede merge.

## Evidence

- Independent semantic reference inventory at A14d5950 and first consumer a9df5cd3 finds this unconditional file-scoped namespace owner. Its private call chain does not depend on the CrossProject wrappers; no caller or seam is left half-migrated by the first consumer.
- First-consumer PR1846 owns CrossProject raw/generated wrappers plus the new factory and retains all45 original cases. This row is a residual instance of that mechanism, not a new selected repair or a claim it is already fixed.
- Operator Directive6 and updated remediate prompt require this deps-chained remainder in a docs-only PR before the first consumer merges. Root owns filing and closure; implement only after prerequisite delivery and a fresh reviewed plan.

## Scope

- Existing namespace objective decomposed by independent consumer; no added objective or reduction of original CrossProject Acceptance. Later independently correct PR, one owner at a time. Any defect introduced by this migration stays in that PR; different pre-existing mechanisms retain their own rows.
