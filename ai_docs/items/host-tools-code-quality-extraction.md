# host-tools-code-quality-extraction — Extract code-quality tools

**row:** `host-tools-code-quality-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `host-tools-dependency-analysis-extraction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — unused/dead-local/dead-field and duplicate-helper and duplicated-method endpoints.
- (new) `src/RoslynMcp.Host.Stdio/Tools/CodeQualityTools.cs`.
- `tests/RoslynMcp.Tests/AliasRemovalTests.cs:104`
- `tests/RoslynMcp.Tests/DuplicateMethodDetectorTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] Move exactly `find_unused_symbols`, `find_dead_locals`, `find_dead_fields`, `find_duplicate_helpers`, `find_duplicated_methods` with no forwarding wrappers or duplicate registrations.
- [ ] Preserve canonical metadata, parameters/defaults, payloads, and cancellation behavior.
- [ ] One table-driven discovery and behavior matrix proves each live name appears once, `find_duplicated_code` stays absent, and direct-call behavior is preserved.

## Evidence

- The five live endpoints form the file's code-quality cluster and share alias/smoke tests distinct from dependency and search tools.

- PR #1883 and ADR 0011 removed the old duplicate-code alias. Preserve the [historic alias parity suite](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/8f5a8e09e3124a1804944d2f79f1141705ad514c/tests/RoslynMcp.Tests/AliasToolsTests.cs) as pre-v5 evidence.
