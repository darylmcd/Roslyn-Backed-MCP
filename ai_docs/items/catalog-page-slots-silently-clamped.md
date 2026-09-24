# catalog-page-slots-silently-clamped — Reject out-of-range catalog page slots

**row:** `catalog-page-slots-silently-clamped` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs:272`

## Acceptance

- [ ] roslyn://server/catalog/tools/-1/0 → -32602 naming offset/limit
- [ ] …/tools/abc/5 names 'offset must be an integer'

## Evidence

- catalog/tools/-1/0 → OK offset 0 limit 1; catalog/tools/0/999 → limit 200; catalog/tools/abc → generic redacted error. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
