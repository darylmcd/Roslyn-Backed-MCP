# catalog-diff-advertised-pair-rejected — Make catalog-diff advertise only accepted pairs

**row:** `catalog-diff-advertised-pair-rejected` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs:80`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs:299`

## Acceptance

- [ ] Every pair named in the resource description resolves
- [ ] The InvalidArgument message names the supported pairs

## Evidence

- catalog-diff/v2.3.1/v2.3.2 → 'Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource.' — the catalog lists no pairs; only 2.3.1→current/latest works. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
