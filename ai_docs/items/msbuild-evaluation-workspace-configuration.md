# msbuild-evaluation-workspace-configuration — Evaluate the loaded configuration

**row:** `msbuild-evaluation-workspace-configuration` · **pri:** `Medium` · **size:** `M` · **deps:** `restore-callers-missing-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs`: GetOrLoadProject.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`: source evaluation properties.
- `tests/RoslynMcp.Tests/MsBuildEvaluationServiceTests.cs`
- `tests/RoslynMcp.Tests/MsBuildEvaluationCacheTests.cs`

## Acceptance

- [ ] Property/item queries evaluate with the loaded workspace configuration and per-target identity; retain cache ownership/version invalidation.
- [ ] Observe original custom-configuration property/item regression fail; prove same-file distinct configured sessions remain isolated and reload updates results.
- [ ] Do not render or log global-property values; preserve existing redaction contract.

## Evidence

- Source-only inspection2026-10-15: GetOrLoadProject creates ProjectCollection and LoadProject(filePath) without the source global properties, although callers select the loaded Roslyn project. A custom-config loaded workspace can disagree with query defaults. No runtime or completion claim.
