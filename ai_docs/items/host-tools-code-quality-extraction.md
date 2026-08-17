# host-tools-code-quality-extraction — Extract code-quality tools

**row:** `host-tools-code-quality-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `host-tools-dependency-analysis-extraction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — unused/dead-local/dead-field and duplicate-helper/method/code endpoints.
- New `src/RoslynMcp.Host.Stdio/Tools/CodeQualityTools.cs`.
- `tests/RoslynMcp.Tests/AliasToolsTests.cs`
- `tests/RoslynMcp.Tests/DuplicateMethodDetectorTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] Move exactly `find_unused_symbols`, `find_dead_locals`, `find_dead_fields`, `find_duplicate_helpers`, `find_duplicated_methods`, and `find_duplicated_code` with no forwarding wrappers or duplicate registrations.
- [ ] Preserve canonical/alias metadata, parameters/defaults, payloads, and cancellation behavior.
- [ ] One table-driven canonical/alias/smoke matrix proves every name appears once and direct-call behavior is unchanged.

## Evidence

- The six endpoints form the file's code-quality cluster and share alias/smoke tests distinct from dependency and search tools.
