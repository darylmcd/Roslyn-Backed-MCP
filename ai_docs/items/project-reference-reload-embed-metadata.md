# project-reference-reload-embed-metadata — Preserve evaluated embed metadata when loading references

**row:** `project-reference-reload-embed-metadata` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`
- `Directory.Packages.props:14`

## Acceptance

- [ ] Reproduce XML EmbedInteropTypes=true becoming false in a real isolated workspace before preview redemption; retain the true expectation.
- [ ] Derive evaluated reference metadata without guessing from raw XML; qualify imports, conditions, aliases and target mapping.
- [ ] Prefer an upstream Roslyn correction or supported evaluated metadata seam; do not import held native authority machinery without separate review.
- [ ] Preserve metadata through initial load, reload and preview redemption; preserve unrelated text and project bytes.
- [ ] Observe old behavior red, obtain cold review and scoped/hosted validation, merge and complete the serialized delivery gate before closure.

## Evidence

- 2026-10-23 reference-rebase-reload-diagnosis.log: 1 failed, 2 passed; the precondition expected embed=true but actual=false before public apply.
- Preserved exact failing test and fixture: commit f18f5d5934d2e898af1816059b473c7ab5c77b75 on codex/preview-project-reference-metadata-rebase, PreviewTokenCrossCouplingTests.cs Reloaded_ProjectReference_Metadata_Uses_Semantic_Alias_Equality("alias-order-desired"). No suppression or weakened expected-true assertion qualified this defect.
- Local receipt: <user>/.codex/scratch/roslyn-remediate-20261023-split/reference-rebase-reload-diagnosis.log and reference-rebase-with-reload-embed-reproduction.cs.txt.
- Installed Microsoft.CodeAnalysis.Workspaces.MSBuild5.9.0 nuspec pins upstream commit35d9211b841e7613c1d2f8f5af6d628ace696c4c; MSBuildProjectLoader.Worker_ResolveReferences.cs325–329 constructs new ProjectReference(to, aliases), omitting EmbedInteropTypes.
- https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.Worker_ResolveReferences.cs#L325

## Context

- External dependency mechanism; the snapshot rebase correction remains independent and retains embed mutation/conflict coverage without reload.
- Keep this row open. Do not describe reload/embed as qualified by the rebase slice.
