# composite-preview-truncation-authority — composite-preview-truncation-authority

**row:** `composite-preview-truncation-authority` · **pri:** `Medium` · **size:** `L`

| Field | Value |
|---|---|
| Suggested id | composite-preview-truncation-authority |
| Priority / size | Medium / L |
| Mechanism | Composite mutation authority does not carry whether the displayed diff was truncated. |
| Existing-row check | preview-store-explicit-internal-state covers ordinary IPreviewStore producer state; scaffold-batch-preview-apply-route covers BatchTestScaffolder routing. Neither acceptance names ICompositePreviewStore or its persisted Entry. |
| Evidence | src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs:98 emits per-file truncation; Helpers/SolutionDiffHelper.cs:94 emits aggregate sentinel. Services/ExtractAndWireOrchestrator.cs publishes every mutation independently of bounded changes. Core/Services/ICompositePreviewStore.cs:21 and Roslyn/Services/CompositePreviewStore.cs:24 omit rendering state. Services/CompositeApplyOrchestrator.cs:49-75 claims the entry and begins authority acquisition without a truncation gate. |
| Verification | Static trace only; no execution or exploit claim. |

## Acceptance

- Refuse every bounded composite preview before Store when the actual per-file renderer or aggregate renderer reports truncation; retain existing caps.
- Reuse the exact marker detector used by ordinary PreviewStore. No new force parameter, optional completeness default, or persistence schema is introduced.
- Cover ClassSplitOrchestrator, PackageMigrationOrchestrator, ExtractAndWireOrchestrator, and both bounded SymbolRefactorService routes; check each batch step before aggregation can overwrite a marker.
- Prove actual per-file and aggregate producer refusal, zero Store calls and persisted authority on refusal, unchanged disk, and complete persisted cross-store redemption. Both producer regressions must fail with the corresponding guard removed.
- Distinguish existing historical tokens, whose original displayed completeness is not recoverable, from newly issued tokens. Preserve normal TTL; do not fabricate historical evidence.
- Current-session evidence: composite67 PASS7; guarded class68 and aggregate69 each fail the expected missing-refusal assertion with its guard removed; sources restored exactly; combined bindings70 PASS9 includes all7 composite controls.

## Scope

- One regression shape: bounded displayed preview and persisted mutation authority disagree.
- Advisory anchors: ICompositePreviewStore, CompositePreviewStore, PersistentCompositeStorage, CompositeApplyOrchestrator; migrate all producer calls required by the state contract.
- Not covered by consumer-reference topology or linked-file mutation deduplication. Parent must determine immediate prerequisite scope under Directive 6 before shipping an exposed unsafe route.
