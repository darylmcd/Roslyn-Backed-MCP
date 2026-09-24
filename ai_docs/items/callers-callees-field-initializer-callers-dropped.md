# callers-callees-field-initializer-callers-dropped — Resolve the containing symbol of field initializers via the declarator

**row:** `callers-callees-field-initializer-callers-dropped` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolServiceHelpers.cs:25`

## Acceptance

- [ ] callers_callees(ServerSurfaceCatalog.Tool) reports the initializer callers (find_references: 175)
- [ ] find_references containingMember is non-null for initializer refs
- [ ] impact_analysis affected declarations > 0 for Tool

## Evidence

- callers_callees(Tool 434:33) → totalCallers 0 vs find_references 175; impact_analysis 175 refs / 0 affected declarations. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
