# change-signature-add-skips-same-document-impls — Make change_signature_preview op=add update every implementation and callsite in a shared document

**row:** `change-signature-add-skips-same-document-impls` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:71`

## Acceptance

- [ ] op=add on an interface member whose implementations live in the same file updates every implementation (no CS0535)
- [ ] callsiteUpdates equals find_references callsite count
- [ ] Regression test: interface + 2 implementations + 3 callers in one file

## Evidence

- change_signature_preview op=add IG4Shape.Area → CS0535 on G4Square/G4Circle after apply; callsiteUpdates=1 of 5. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Original-tree spans are resolved against the already-edited document, so nodes after the first edit are silently not found.
