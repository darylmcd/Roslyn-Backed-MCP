# restore-required-project-inventory — Restore the complete evaluated dependency inventory

**row:** `restore-required-project-inventory` · **pri:** `Medium` · **size:** `M` · **deps:** `fork-document-text-encoding`

## Anchors

- (new) `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:644`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:378`
- (new) `src/RoslynMcp.Roslyn/Services/RestoreProjectGraph.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs`
- `tests/RoslynMcp.Tests/TUnitMtpNativeTestRunTests.cs`

## Acceptance

- [ ] Source restore callers use one complete required-project plan: capture nullable package roots with platform identity from the evaluated transitive closure, including ReferenceOutputAssembly=false targets absent from Roslyn; restore a uniform-root target once or each mixed-root project with --no-dependencies under one total timeout/cancellation budget.
- [ ] Preserve test-runner restore-before-resolved-version authorization and subsequent --no-restore execution; retain configured globals/framework contexts and credential redaction.
- [ ] Prove the old public caller rewrites a referenced project's real local-feed assets to another cache and omits a real non-compilation .proj chain; pass independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- Four production files, at most three test files and one fragment. One plan-completeness contract: mixed --no-dependencies cannot ship before the dependency inventory is complete.
- Share the minimal evaluator with later fork protection; do not add unused fork-preflight machinery.
- Dependency on document encoding records ordered delivery, not a technical prerequisite.

## Evidence

- restore-fork-closure-corrected-red.log: real public caller changed packages-A to packages-B; real A-to-B-to-C .proj closure expected three restores but produced one. Omitted targets were verified absent from Roslyn inventory.
- restore-complete-callers-json-red.log reproduced recorded-root argv and exhausted-budget failures.
- Re-derive ea969292; preserve all source caller instances and nullable roots. No BuildHost/certificate/lease imports.
