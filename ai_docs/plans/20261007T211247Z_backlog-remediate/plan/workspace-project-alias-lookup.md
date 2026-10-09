| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `WorkspaceManager.cs:985-1020` combines project names and lexical paths in one OrdinalIgnoreCase index, then looks up the raw caller spelling. Existing PhysicalPathResolver provides physical alias identity. |
| Approach | - [ ] A project selected by path matches the loaded project when its argument uses an equivalent junction, drive alias, or canonical drive spelling.<br>- [ ] Name lookup preserves its current behavior; path lookup uses physical filesystem identity with platform comparison.<br>- [ ] A regression loads a project through one path spelling and selects it through another without selecting a different project. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`. Tests 1: `tests/RoslynMcp.Tests/WorkspaceProjectAliasLookupTests.cs`. Own fragment; no deletions. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Preserve current name lookup and separate physical path lookup with FileSystemPath comparison. Re-derive path/name collisions and versioned index invalidation; do not change shared path helpers. The private ProjectIndexEntry has one defining-file construction/consumer mechanism. |
| Validation | Red-first real loaded project selected through a Windows junction/drive alias or supported symlink; wrong project and case-distinct paths stay distinct on applicable platforms. Explicitly skip unavailable filesystem features. Run WorkspaceProjectAliasLookupTests and existing GetProject/dedup/version tests. Per-edit compile_check and targeted test_run; scoped regression gate, then serialized complete local `just ci` through the sanctioned full-gate producer (complete addenda ci_equivalent), followed by required hosted validate per CI_POLICY.md. |
| Performance review | N/A - correctness fix; inspect alias-resolution cost at index rebuild, not repeated enumeration. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Workspace project selection resolves equivalent physical path aliases. |
| Backlog sync | Close rows: [workspace-project-alias-lookup]. |
