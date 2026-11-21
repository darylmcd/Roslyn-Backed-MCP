# change-signature-preview-builder-build-async-decomposition — Decompose ChangeSignatureAddRemovePreviewBuilder.BuildAsync

**row:** `change-signature-preview-builder-build-async-decomposition` · **pri:** `Low` · **size:** `S` · **verified:** `02fddfa9500d30848b8f5e34367322db1011b3df` · **deps:** `change-signature-unrelated-invocation-rewrite`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:239`

## Acceptance

- [ ] BuildAsync split into named steps with no behaviour change; existing change-signature tests pass unchanged.
- [ ] Sequence after `change-signature-unrelated-invocation-rewrite`, which edits the same method.
- [ ] Also covers `MaterializedTypeName` (same file, nesting depth 8, complexity 32) and `ValidateCandidateAsync` (complexity 36) if they are still hotspots.

## Evidence

- Surface audit 2026-11-20 (server 5.1.1), get_complexity_metrics: BuildAsync CC 108 at line 239; second-highest in the solution.
