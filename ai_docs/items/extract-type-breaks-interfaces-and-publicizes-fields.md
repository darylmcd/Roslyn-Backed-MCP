# extract-type-breaks-interfaces-and-publicizes-fields — Guard extract_type_preview against moving interface members and preserve accessibility

**row:** `extract-type-breaks-interfaces-and-publicizes-fields` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:127`

## Acceptance

- [ ] Moving a member that implements an interface is refused or leaves a forwarder (no CS0535)
- [ ] Private fields stay private (:1024)
- [ ] Unchanged regions are not reformatted (no whole-file NormalizeWhitespace)

## Evidence

- extract_type_preview moving Lookup → CS0535; fields made public; whole file NormalizeWhitespace'd. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
