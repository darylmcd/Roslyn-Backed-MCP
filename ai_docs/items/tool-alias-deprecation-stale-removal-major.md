# tool-alias-deprecation-stale-removal-major — Refresh alias deprecation metadata

**row:** `tool-alias-deprecation-stale-removal-major` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs:49`

## Acceptance

- [ ] earliestRemovalMajor is a future major (or the aliases are removed per policy)
- [ ] A test asserts earliestRemovalMajor > current major

## Evidence

- get_test_coverage_map deprecation.earliestRemovalMajor=2; server 4.2.1. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
