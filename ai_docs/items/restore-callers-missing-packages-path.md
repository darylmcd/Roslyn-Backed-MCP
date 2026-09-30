# restore-callers-missing-packages-path — Pass the assets-recorded packagesPath in the test-runner and fork-apply restores

**row:** `restore-callers-missing-packages-path` · **pri:** `Medium` · **size:** `S` · **deps:** `workspace-restore-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:317`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:526`
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:387-441`
- (new) `tests/RoslynMcp.Tests/RestorePackagesPathCallersTests.cs`

## Acceptance

- [ ] The test-runner restore (`TestRunnerService.cs:317`) and the fork-apply restore (`WorkspaceForkApplyService.cs:526`) pass the assets-recorded `project.restore.packagesPath` to `dotnet restore` via the helper added by `workspace-restore-packages-path`, so a worktree scratch package folder is preserved.
- [ ] Red-first fake-runner test per caller proves `--packages <recorded path>` appears in the restore arguments, and that no flag is added when no assets file exists.

## Evidence

- Deepener finding for `workspace-restore-packages-path` (plan 20260930T213336Z): both call sites run `dotnet restore` with the same missing `--packages` argument as the load/reload helper that row fixes.

## Context

- Follow-on of `workspace-restore-packages-path`; reuse its `RestoreStalenessDetector.TryReadRestorePackagesPath` helper instead of duplicating the assets parsing.
