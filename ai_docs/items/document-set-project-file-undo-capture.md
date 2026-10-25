# document-set-project-file-undo-capture — Capture every physical before-image for document mutations

**row:** `document-set-project-file-undo-capture` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:415`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1093`
- (new) `tests/RoslynMcp.Tests/RefactoringPhysicalUndoCaptureTests.cs`

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:27`

## Acceptance

- [ ] Capture physical before-bytes before native source writes for changed-document-only previews, including body and encoding-only changes; capture exact original physical bytes even when original document text is uncached, and restore exact undo bytes.
- [ ] Capture each existing project file affected by shared renderer document additions/removals or reference changes; deduplicate the actual physical paths through the existing snapshot helper.
- [ ] Observe old-code failures for explicit Compile add/remove and explicit UTF-16 body/encoding changes; preserve project Unicode comments, encoding, original membership and exact source/project bytes after undo.
- [ ] Preserve current source identity/version, preview authorization, no-op/reference behavior and failure rollback; use the existing renderer mutation predicate and undo capture pipeline without rerendering or new undo APIs.
- [ ] Pass fresh independent review, serialized affected tests and hosted Windows/Linux/SDK-floor checks before closure.

## Evidence

| Evidence | Result |
|---|---|
| Unchanged main c3274dce native-policy matrix | 24 cases, 18 failed and six passed. In each of three SDK declaration shapes, EnableDefaultCompileItems=false correctly produced one Compile item, but undo left that item after deleting the added source file. Other policy failures are a separate mechanism. |
| Previous source-only causal cohort | Changed-only body/encoding previews had no physical before-capture; lazy old snapshots read new bytes after apply. Preserved in PR1826 source-undo provenance receipts. |
| Root cause | PrepareApply omits changed-document-only capture admission; BuildFileSnapshots captures project XML only for reference changes although document add/remove rendering also mutates it. |
| Dependency | PR1826 cannot satisfy required source apply/undo preservation without this independently correct fix. Extract its existing changed-only admission fix here before rebasing the graph work. |

- Scoped qualification: unchanged c327 focused tests 10 FAIL / 1 PASS; corrected identical tests 11 PASS; final exact 18-class serialized cohort 328 PASS / 0 FAIL / 0 SKIP in 7m58s with zero overlapping individual intervals. Independent actual review 0 BLOCKING; hosted qualification remains pending.

## Context

- Read-only dependency: existing ProjectReferenceMutationRenderer.HasChanges; no renderer source edit or visibility change is required.

- Reference branch: codex/restore-fork-reviewed-project-graph, published checkpoint 0062d60c; raw evidence is linked from PR1826.
- Preserve original stash codex-remediate-20261022-preserve-restore and PR1806/PR1809; they are references only.
- The additional production owner is only the exposed stale UndoService capture-contract comment; no Undo API or behavior changes. Effective native default-item policy is a separate renderer decision. Do not change it or normalize null encodings in this slice.
- The remediation root owns literal just ci before aggregate closure and final merged-main just ci after trailing docs.
