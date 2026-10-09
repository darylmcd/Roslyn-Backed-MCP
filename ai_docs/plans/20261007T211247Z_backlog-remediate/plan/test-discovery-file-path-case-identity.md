| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `TestDiscoveryService.cs:277,724,733,894` uses OrdinalIgnoreCase for exact file identity, merging distinct files on case-sensitive volumes. Name/search heuristics legitimately remain case-insensitive. |
| Approach | - [ ] File-path sets and exact file-path comparisons use platform file identity. Preserve case-insensitive search terms and display-name matching.<br>- [ ] On a case-sensitive volume, two loaded files whose names differ only by case remain distinct in related-test discovery and direct-reference selection.<br>- [ ] On Windows, path casing variations for one file still select the same tests. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`. Tests 1: `tests/RoslynMcp.Tests/TestDiscoveryFileIdentityTests.cs`. Own fragment; no deletions. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Use existing FileSystemPath.Comparer/Comparison for exact path collections only; preserve search terms, display names and test identities. Probe all same-file path collections/comparisons. Isolate fixture roots and explicitly skip unsupported filesystem shapes. |
| Validation | Red-first real related-file/reference discovery using case-distinct files on a case-sensitive volume; Windows casing aliases still identify one file. Run the new TestDiscoveryFileIdentityTests and related test-discovery classes on Windows and Linux. Per-edit compile_check and targeted test_run; scoped regression gate, then serialized complete local `just ci` through the sanctioned full-gate producer (complete addenda ci_equivalent), followed by required hosted validate per CI_POLICY.md. |
| Performance review | Preserve the existing path-set lookup structure; change comparison semantics without adding discovery scans. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Related-test discovery preserves platform-specific file path identity. |
| Backlog sync | Close rows: [test-discovery-file-path-case-identity]. |
