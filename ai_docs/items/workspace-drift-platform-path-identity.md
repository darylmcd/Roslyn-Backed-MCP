# workspace-drift-platform-path-identity — Preserve case-distinct paths in drift results

**row:** `workspace-drift-platform-path-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceDriftService.cs:90`
- `tests/RoslynMcp.Tests/Services/WorkspaceDriftServiceTests.cs`

## Acceptance

- [ ] Use the repository filesystem-aware path comparer for distinct document paths.
- [ ] On a case-sensitive filesystem, prove two case-distinct drifted files both survive while linked duplicates collapse; retain deterministic ordering and Windows behavior.
- [ ] Observe the regression fail on current code before fixing it; keep timestamp tests deterministic.

## Evidence

- Adjacent source inspection found unconditional OrdinalIgnoreCase deduplication of physical document paths; Linux case-distinct drifted files collapse. Static finding; no runtime reproduction performed in this audit.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
