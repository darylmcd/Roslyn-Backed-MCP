# tool-alias-deprecation-stale-removal-major — Refresh alias deprecation metadata

**row:** `tool-alias-deprecation-stale-removal-major` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs:49`

## Acceptance

- [ ] earliestRemovalMajor is a future major (or the aliases are removed per policy)
- [ ] A test asserts earliestRemovalMajor > current major

## Evidence

- get_test_coverage_map deprecation.earliestRemovalMajor=2; server 4.2.1. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`

## Notes

2026-10-27: policy (docs/release-policy.md:45-46) removes deprecated aliases in the next major, so remove the three 1.33.0 aliases at 5.0.0 rather than retargeting EarliestRemovalMajor, and decide apply_composite_preview (=5) in the same change. Blast radius: hooks/hooks.json:14,18 (release-managed), skills refactor/modernize/migrate-package/refactor-loop (apply_composite_preview), surface-test prompts (quick.md:96, setup-and-analysis.md:131, apply-and-test.md:153), AliasToolsTests.cs:165, SurfaceCatalogTests.cs:187,278; refresh the .ai-doc-audit.md Live Surface snapshot and README counts (175 tools). Most get_symbol_outline hits in skills are the Jedi name-collision boilerplate, not Roslyn alias calls.

2026-10-28 operator decision: remove all four aliases at 5.0.0 (get_symbol_outline, find_duplicated_code, get_test_coverage_map, apply_composite_preview); do not retarget EarliestRemovalMajor. Covered by ADR 0011 with a migration note naming document_symbols, find_duplicated_methods, test_coverage and apply_composite. Re-sized S to M for the blast radius listed above (release-managed hooks/hooks.json edit needs the sentinel in the executor own checkout). Refresh the Live Surface snapshot and README counts in the same PR.
