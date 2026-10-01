# host-bind-options-locals-untested — Extract and test the remaining Program.cs Bind*Options local functions

**row:** `host-bind-options-locals-untested` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Configuration/ValidationServiceOptionsEnvironmentBinder.cs`
- `tests/RoslynMcp.Tests/ValidationServiceOptionsEnvironmentBinderTests.cs`

## Acceptance

- [ ] The remaining top-level `Bind*Options` local functions in `Program.cs` (workspace, preview store, execution gate options) are extracted to internal static binder classes in `Configuration/`, mirroring `ValidationServiceOptionsEnvironmentBinder` and `ScriptingOptionsEnvironmentBinder`, with the same behavior.
- [ ] Each binder has tests for valid, unset, non-positive and non-numeric environment values keeping their defaults.

## Evidence

- Plan 20261001T034545Z (`workspace-restore-budget`): the fixer extracted `BindValidationServiceOptions` to make the new `ROSLYNMCP_RESTORE_TIMEOUT_SECONDS` binding testable; the other `Bind*Options` locals remain untestable top-level locals in `Program.cs`.

## Context

- Follow-on of `workspace-restore-budget`; one regression shape per binder, so size M.
