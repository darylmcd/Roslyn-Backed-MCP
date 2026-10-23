# fork-evaluated-source-isolation — Reject copied graph paths into the source workspace

**row:** `fork-evaluated-source-isolation` · **pri:** `Medium` · **size:** `M` · **deps:** `restore-required-project-inventory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:128`
- (new) `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- (new) `src/RoslynMcp.Roslyn/Services/RestoreProjectGraph.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/ForkGraphPreparationTests.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`

## Acceptance

- [ ] Before any restore/runner effect, evaluate the actual copied transitive project inventory and reject imports, file inputs and output paths that resolve into the source workspace, including non-compilation dependencies absent from Roslyn and relevant framework/global contexts.
- [ ] Exercise this protection with an unchanged preview graph; retain valid external SDK imports and bookkeeping identities while preserving cancellation and source bytes/version/token authority.
- [ ] Observe a real hidden .proj output escape reach the old runner; obtain independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- At most three production files, two test files and one fragment. Ship active isolation protection, not evaluator scaffolding.
- Reuse the dependency evaluator; no second evaluator, physical-directory lease or native compiler certificate.

## Evidence

- restore-fork-closure-corrected-red.log: a hidden non-compilation .proj with an output into the original source reached the forbidden runner; absence from Roslyn was asserted.
- Graph fidelity is independently owned by dependent restore-fork-reviewed-project-graph.
