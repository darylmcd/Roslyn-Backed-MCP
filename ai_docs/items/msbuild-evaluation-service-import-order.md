# msbuild-evaluation-service-import-order — Normalize evaluation service imports

**row:** `msbuild-evaluation-service-import-order` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:1` — imports through line9 mix namespace groups and aliases outside formatter order.
- `eng/format-baseline.json:622` — one tracked IMPORTS allowance for this file.

## Acceptance

- [ ] Normalize this file's imports using the repository formatter; preserve aliases, symbol binding and executable behavior.
- [ ] Remove only this resolved baseline allowance through the documented baseline procedure; preserve unrelated baseline entries and all checks.
- [ ] Verify the affected formatter result changes from one tracked IMPORTS finding to zero, then compile and run the affected evaluation-service contracts. No mirrored source-text test is needed.
- [ ] backlog: sync ai_docs/backlog.md.

## Evidence

- Rechecked on e17fae0d and reviewed candidate920c650f during metadata ownership remediation: changed-file formatter passed with zero new findings and one tracked IMPORTS finding in this file. Its import block is unchanged by the ownership patch.
- Raw source and formatter output establish formatting debt only; no unused import, runtime defect or behavior regression is claimed.
- This is one bounded repair child of the inventory objective in `items/repository-dotnet-format-baseline-partition.md`; do not expand it into a repository-wide sweep.
