# build-command-captured-evaluation-context — Build the loaded configuration

**row:** `build-command-captured-evaluation-context` · **pri:** `Medium` · **size:** `M` · **deps:** `restore-callers-missing-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BuildService.cs`: PrepareWorkspaceBuildAsync and PrepareProjectBuild create plain build arguments without captured global properties.
- `src/RoslynMcp.Core/Services/IBuildService.cs`: BuildCommandPlan.
- `tests/RoslynMcp.Tests/BuildTestToolsShimTests.cs`
- `tests/RoslynMcp.Tests/ValidationIntegrationTests.cs`

## Acceptance

- [ ] Capture the loaded evaluation configuration immutably in workspace/project build plans and preserve it through execution after reload.
- [ ] Observe original custom-configuration build regression fail; verify actual conditional source/reference compilation and separate configured sessions.
- [ ] Preserve diagnostics parsing/counts before sanitizing captured property values; verify raw and escaped synthetic sentinels cannot reach public output, logs or exception chains.

## Evidence

- Source-only inspection2026-10-15: both BuildService plan factories emit only build, target and --nologo. BuildCommandPlan retains workspace version but no evaluated configuration. No runtime reproduction or completion claim.
- Distinct build-plan mechanism from property/item evaluation queries and the active fork TestRunner replay correction.
