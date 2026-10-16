# project-list-refusals-missing-verbose-status - Give usable project-list recovery

**row:** `project-list-refusals-missing-verbose-status` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Roslyn/Helpers/ProjectFilterHelper.cs`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/ProjectFilterUnknownNameScanTests.cs`

## Acceptance

- [ ] Every project-list correction includes workspace_status with verbose=true; summary mode cannot supply project names.
- [ ] Preserve error categories and identities; verify actual public guards and verbose project discovery.
- [ ] Inventory all copies; correct the ten existing messages plus the newly authored test-run remedy in one complete change.

## Evidence

- Current-session source inventory found ten incorrect copies across project filters, compile checks, scaffolding, file operations, Fix All, MSBuild, parameter objects, generic error projection, symbol relationships and workspace project resolution.
- The new test-run remedy exposed the same omission during cold review. This companion is additional to the selected fifteen rows; file count reflects one copied recovery mechanism.

Expanded current-session inventory: fourteen existing project-list/support/location hints across eleven production files require verbose=true, plus the newly authored test-run remedy. Broader source scan caught CompileCheckService restore hints and the second scaffolding refusal. Existing guards produced15old-red failures; exact original CompileCheckService produced3additional old-red failures. Preserve categories and semantics; the companion remains conditional on final verification/merge.
