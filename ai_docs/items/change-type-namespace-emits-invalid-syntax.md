# change-type-namespace-emits-invalid-syntax — Emit valid namespace syntax in change_type_namespace_preview

**row:** `change-type-namespace-emits-invalid-syntax` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:371`

## Acceptance

- [ ] Preview on a file-scoped-namespace file produces compilable output (apply_with_verify status=applied)
- [ ] Regression test covers file-scoped and block-scoped sources

## Evidence

- change_type_namespace_preview emits 'namespaceSampleLib.Shapes{'; apply_with_verify → 8 errors, rolled back. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
