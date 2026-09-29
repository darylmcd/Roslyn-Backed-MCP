# workspace-restore-packages-path — Preserve the assets package folder

**row:** `workspace-restore-packages-path` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:640-680`
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:218-268`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs`

## Acceptance

- [ ] When a project already has an assets file, server-side restore targets its `project.restore.packagesPath` instead of the host process package directory.
- [ ] A red-first fake-command-runner test verifies the restore arguments preserve a worktree scratch package folder.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:668-672` invokes `dotnet restore` without the assets-recorded packages path.

## Context

- Independent data-integrity fix for explicit restore and prerequisite for the default-on slice.
