# Parameter-object preview — design note
<!-- purpose: Bounded design for a parameter_object_preview MCP tool. Splits implementation into sized backlog rows. -->

**Initiative:** `parameter-object-preview-design`
**Date:** 2026-05-07
**Author:** feature-dev session (cold context)
**Scope:** design-only; no `src/` mutations.
**Outcome:** see [Verdict](#verdict) — **go** for v1 (positional-record only), implementation split into two sized backlog rows.

---

## (a) Use case

The recurring need is replacing N parameters of a method with one record/DTO and updating every call site. This is broader than `change_signature_preview` (which supports add/remove/rename but not reorder or grouping) and broader than `replace_invocation_preview` (which rewrites method-to-method but does not synthesize a new type). Today the agent has to: scaffold a record by hand (`scaffold_type_preview`), edit the method signature manually, then `replace_invocation_preview` for each call site — three tools, no atomicity, easy to leave the workspace in a partial state if any step fails.

---

## (b) Tool contract

### Input DTO — `ParameterObjectPreviewRequest`

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `workspaceId` | string | yes | — | Standard locator. |
| `target` | `SymbolLocator` | yes | — | Method whose parameters are being grouped. Same locator shape as `change_signature_preview`. |
| `parameterNames` | `string[]` | yes | — | ≥2 parameters; must all exist on the target method; preserves caller-specified order in the generated record. |
| `newTypeName` | string | yes | — | PascalCase identifier; validated by existing `IdentifierValidation.ThrowIfInvalidIdentifier`. |
| `dtoProjectName` | string | no | method's project | Override target project; must already be referenced by every project containing a call site (no auto-`<ProjectReference>` insertion in v1). |
| `dtoNamespace` | string | no | method's containing namespace | Caller override. |
| `dtoFolders` | `string[]` | no | derived from namespace via `ResolveFolderSegmentsForNamespace` | Mirrors `scaffold_type_preview` semantics. |
| `parameterName` | string | no | `"parameter object name"` camelCased from `newTypeName` | Name of the new single parameter on the refactored method. |

### Output DTO

Reuse `RefactoringPreviewDto` unchanged (token, change list, callsite-update counts, warnings). The added file appears as a `FileChangeDto` with status `Added`. No new response shape required.

### Apply path

No new apply tool. The existing generic `apply_refactoring` consumes the preview token. `RefactoringService.ApplyRefactoringAsync` already handles `hasDocumentSetChanges` (added documents) atomically — verified in `MoveTypeDiskStateTests`.

---

## (c) Generated DTO conventions

- **Type kind:** positional `record` (hardcoded for v1). Idiomatic, immutable by default, primary constructor preserves the positional call shape, and value-equality is free. No `typeKind` parameter — the v1 contract is opinionated.
- **Member shape:** primary-constructor parameters mirror the original parameter list in the order specified by `parameterNames`, preserving each parameter's type and nullable annotation. No XML doc comments synthesized in v1 (call `/document` afterward if needed).
- **Modifiers:** `sealed record`. Visibility is derived as follows:
  - **Same-project path** (`dtoProjectName` unset or equals method's project): match the containing type's visibility (`public sealed record` or `internal sealed record`).
  - **Cross-project path** (`dtoProjectName` differs): force `public sealed record`. An `internal` record in a different assembly cannot be referenced by the method's signature, so matching the containing type would produce an unbuildable solution. The cross-project pre-flight (§(e)) does not catch this on its own — the visibility decision must be made here.
- **Namespace:** caller-overridable; default = the method's containing namespace (matches `extract_type_preview`). When `dtoFolders` is supplied without `dtoNamespace`, the namespace is derived from the project's default + folder segments (matches `scaffold_type_preview`).
- **File:** `{newTypeName}.cs` under the resolved folder. Path validated against project root by `ClientRootPathValidator.ValidatePathAgainstRootsAsync` (same guard as `create_file_preview`).
- **Using directives:** none synthesized; the record uses fully qualified names from the original parameter list. Post-creation `organize_usings_preview` is the documented follow-up.

---

## (d) Call-site rewrite policy

All rewrites operate on `InvocationExpressionSyntax` call sites of the original method `M`. The new `Dto` is being **created** by this preview, so no constructor call sites exist yet — the rewrite synthesizes one `ObjectCreationExpressionSyntax` sub-expression per call site and substitutes it for N arguments inside the existing `M(...)` invocation. The pipeline reuses the **ordering logic** of `BulkRefactoringService.ReorderWithNamedArguments` (positional/named classification + semantic-order materialization) but the rewrite **shape** is new — extract subset of `M`'s arguments → reorder to `parameterNames` order → wrap as `ObjectCreationExpression(IdentifierName(newTypeName), ArgumentList(...))` → splice back into `M`'s argument list. A new `ParameterObjectInvocationRewriter` helper is required; it is not a simple call into the existing helper.

**Positional call sites.** `M(1, 2, 3)` where `parameterNames = ["a","b","c"]` → `M(new Dto(1, 2, 3))`.

**Named call sites.** `M(c: 3, a: 1, b: 2)` → `M(new Dto(1, 2, 3))`. The positional-record constructor takes positional args at the call site; named args at the original `M` call are flattened to semantic order before wrapping. **No object-initializer syntax is synthesized in v1**; positional records do not expose a parameterless constructor, so initializer syntax would not compile.

**Mixed call sites.** `M(1, c: 3, b: 2)` → `M(new Dto(1, 2, 3))`. Same pipeline.

**Default-value call sites — REFUSE.** When a call site omits a grouped parameter and relies on its default (`M(a: 1, c: 3)` with `b` having `= 0`), the rewrite would have to pick a literal for the omitted `b`. The gap is **statically detectable**, and silently emitting `new Dto(1, 0, 3)` would semantically change the call's resilience to future default changes. Refusing forces the agent to either add the explicit argument first or shrink `parameterNames`. One structured warning per affected call site (file:line + missing parameter name) is returned alongside the refusal.

**`ref` / `out` / `in` parameters — REFUSE entire preview.** Positional records cannot carry by-ref semantics. `InvalidArgument` with the offending parameter name. v1 contract is predictable; partial-grouping deferred.

**`params` parameters — REFUSE entire preview.** Variadic call shape doesn't survive grouping.

**Extension methods.** The `this` parameter MUST NOT appear in `parameterNames` — refuse with `InvalidArgument` if it does. Other parameters of an extension method are eligible; call sites in both reduced (`x.M(a, b)`) and explicit (`Ext.M(x, a, b)`) form are normal `InvocationExpressionSyntax` and are rewritten by the same pipeline.

**Local functions.** `SymbolFinder.FindReferencesAsync` does enumerate local-function call sites in current Roslyn, but the rewrite scope is intra-method and the design has no use case for grouping local-function parameters. Refuse when target is a local function; document.

**Method-group / lambda passing the method by name** are unaffected — no argument list to rewrite. Supported.

**Reflective call sites** (`MethodInfo.Invoke`, `Expression<Func<...>>`) are **structurally undetectable** by `SymbolFinder` — the gap cannot be flagged statically. WARN, consistent with `change_signature_preview`'s policy. The detectability vs. silent-semantic-change distinction is what separates REFUSE (default-value sites) from WARN (reflective sites).

---

## (e) Cross-project policy

Cross-project call-site rewriting follows `change_signature_preview`'s **mechanical** model — `SymbolFinder.FindCallersAsync` is called against the full solution and call sites in every loaded project are eligible. However, `ChangeSignatureAddRemovePreviewBuilder` **does not** verify that the modified declaration remains visible to its callers; it produces edits regardless and trusts that an existing method's visibility hasn't changed. That assumption breaks here: the new tool introduces a **new type** that may not be reachable from every caller's project.

The pre-flight project-reference check is therefore **new logic, not inherited.** For every project `P` containing at least one call site of the target method:

1. If `P == dtoProject`, OK.
2. Else check `Project.AllProjectReferences` (transitively) on `P` for an entry pointing at `dtoProject`. If absent, accumulate `(P.Name, dtoProject.Name)` in a missing-references list.
3. If the missing-references list is non-empty after enumerating all caller projects, **refuse the entire preview** with a structured `InvalidArgument` envelope naming each missing reference and recommending `add_project_reference_preview` as the prerequisite. No auto-insertion of `<ProjectReference>` in v1.

Same-project path is friction-free. The cross-project case is supported when the project graph already cooperates and is rejected otherwise with an actionable next step.

---

## (f) Implementation file-count estimate

This is a Rule-3 new-MCP-tool initiative — count structural units, not files. Splitting follows the new-tool exemption shape exactly:

### Initiative A — `parameter-object-preview-tool` (new tool, v1)

| Structural unit | Files |
|---|---|
| Core contract | `src/RoslynMcp.Core/Services/IParameterObjectService.cs` + `src/RoslynMcp.Core/Models/ParameterObjectPreviewRequest.cs` |
| Roslyn implementation | `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (the new builder) |
| Host.Stdio tool surface | `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs` |
| Registration | `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs` entry + `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` DI line |
| **Addenda (mandatory)** | `tests/RoslynMcp.Tests/TestBase.cs`, `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`, `README.md` surface-count |

`productionFilesTouched: 9` (4 structural units / 6 prod files + 3 addenda). `toolPolicy: "edit-only"`. `testFiles: 1` — `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs` covering: (1) positional grouping single-project, (2) named-arg + mixed call sites resolved to semantic order, (3) default-value call site refused with structured warning, (4) `ref`/`out` parameter refused with `InvalidArgument`, (5) cross-project rewrite when reference exists, (6) cross-project refused when reference missing, (7) `apply_refactoring` redeems the token and writes both the new file and the rewritten call sites to disk.

Fits Rule 3 cap (≤4 structural units + addenda). Single bounded initiative.

### Initiative B — `parameter-object-preview-experimental-promotion` (deferred, post-v1)

`promote-tier` flip from `experimental` → `stable` once: (1) external session evidence shows the tool resolves the recurring need; (2) `change_signature_preview` reorder gap (`change-signature-reorder-preview`) is decided so the surface story is coherent. **Do not file this row yet** — the v1 ship is the trigger.

---

## (g) What this design explicitly does NOT do

- **No same-method parameter reorder.** That's `change-signature-reorder-preview`'s scope. This tool's grouping operation reorders only the grouped subset (into the record's positional order); the method's remaining parameters keep their original order.
- **No `class` or `struct` generation.** Hardcoded `record`. v2 can add `typeKind` if asked.
- **No primary-constructor record edits via `change_signature_preview`.** Already a known gap in `ChangeSignatureAddRemovePreviewBuilder` (matches only `BaseMethodDeclarationSyntax`); to be tracked separately if it becomes friction.
- **No auto-`<ProjectReference>` insertion** for cross-project cases. Separate refactoring shape.
- **No XML-doc generation** on the new record. Post-creation `/document` skill flow.

---

## Verdict

**Go for v1.** The design is bounded, the structural pieces all have proven precedents in this codebase, and the hardest sub-question (named-arg call sites) collapses to a clean problem when v1 is restricted to positional-record DTOs. Implementation is a single Rule-3-compliant initiative.

### Next deliverable

File one new backlog row `parameter-object-preview-tool` (priority **Low**; `deps: parameter-object-preview-design`). The row's `do` cell should cite this design note as the contract, list the 4 structural units (6 prod files) + 3 addenda + 1 test file, set `toolPolicy: "edit-only"`, and pin the 7-case regression test shape from §(f). Close `parameter-object-preview-design` in the same PR that adds the new row (`deps` will be vacuously satisfied because the design row will already be removed by the time any planner reads `parameter-object-preview-tool`; the `deps` line is documentation of the prerequisite read for the implementer, not a runtime gate).

---

## Refs

| Path | Why |
|---|---|
| `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs` | Reference for accumulator-pattern preview that rewrites declarations + call sites in one snapshot. |
| `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs` (`ReorderWithNamedArguments`) | Reusable helper for resolving named/positional arguments to semantic order. |
| `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` | Reference for combining `WithDocumentSyntaxRoot` + `AddDocument` in a single preview snapshot. |
| `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` (`ResolveFolderSegmentsForNamespace`) | Namespace→folder mapping. |
| `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`ApplyRefactoringAsync`) | Shared apply path; already handles `hasDocumentSetChanges`. |
| `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs` (`ComputeDocumentFolders`) | Required `folders` arg for `project.AddDocument`. |
| `src/RoslynMcp.Roslyn/Services/PreviewStore.cs` | Token storage + `MaxRedeemableVersion` ceiling. |
| `tests/RoslynMcp.Tests/MoveTypeDiskStateTests.cs` | Regression test shape for "create file + rewrite files" disk-state correctness. |
| `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs` | Assertion-shape template for the new tool's tests. |
| `ai_docs/prompts/backlog-sweep-plan.md` § Rule 3 new-MCP-tool exemption | Sizing rules cited in §(f). |
