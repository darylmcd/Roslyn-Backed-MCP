# 03 — C2 Resources and response contracts

Rows (check C2):

- `flow-analysis-silent-region-narrowing` (Medium) — analyze_data_flow / analyze_control_flow silently analyze a narrower range
- `preview-token-store-mismatch-false-stale` (Medium) — Apply tools report 'workspace reloaded' for tokens from the other preview store
- `catalog-page-slots-silently-clamped` (Low) — Catalog paging resources clamp invalid offset/limit instead of erroring
- `catalog-diff-advertised-pair-rejected` (Low) — catalog-diff description advertises a version pair that is rejected
- `di-registrations-tryadd-omitted-claims-complete` (Low) — get_di_registrations omits TryAdd* but reports totalCountMeaning=complete
- `workspace-status-verbose-not-superset` (Low) — roslyn://workspace/{id}/status/verbose lacks readiness fields the summary has
- `namespace-deps-circular-only-edge-leak` (Low) — get_namespace_dependencies circularOnly returns edges not on any cycle
- `find-type-consumers-no-pagination-metadata` (Low) — find_type_consumers truncates without totalCount/hasMore
- `tool-alias-deprecation-stale-removal-major` (Low) — Alias deprecation metadata says removal in major 2 on a 4.x server

Evidence: `raw/phase-G8.md`, `raw/g8-res-*.txt`, `raw/head-resource-negative.json`. All 14 resources read, and MIME matched content on every read.
