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

## Notes

Surface audit 2026-11-20 (server 5.1.1): only Catalog/Snapshots/catalog-v2.3.1.json ships, so roslyn://server/catalog-diff/5.0.0/current and 1.0.0/current fail (the client shows a generic "Resource not found"); 2.3.1/current works and is 145 KB. docs/release-policy.md step 4 points reviewers at this resource for "supported prior release pairs".
