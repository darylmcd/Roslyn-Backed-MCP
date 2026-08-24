# standalone-test-fixture-restore-contract — Make direct test execution self-preparing

**row:** `standalone-test-fixture-restore-contract` · **pri:** `Medium` · **size:** `M`

## Anchors

- `justfile`
- `eng/verify-release.ps1`
- New `eng/prepare-test-fixtures.ps1`
- `RoslynMcp.slnx`
- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `ai_docs/references/testing.md`

## Acceptance

- [ ] Give direct documented `dotnet test RoslynMcp.slnx` execution one idempotent owner that restores every sample fixture before discovery reaches integration tests.
- [ ] Reuse that owner from release validation instead of retaining a second sample-restore command list.
- [ ] Preserve offline/fail-closed restore diagnostics and do not mutate source-controlled fixture inputs.
- [ ] Add one fresh-cache regression that removes only owned sample `obj` state, runs the documented command, and proves fixture-dependent tests execute rather than fail during load.

## Evidence

An isolated fresh checkout reproduced failures from the documented raw `dotnet test RoslynMcp.slnx` command because sample project assets were absent. `eng/verify-release.ps1` succeeds only because it owns additional sample restore steps that direct contributors do not receive.
