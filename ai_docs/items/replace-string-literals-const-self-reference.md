# replace-string-literals-const-self-reference — Skip the target constant's own initializer in replace_string_literals_preview

**row:** `replace-string-literals-const-self-reference` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:162`

## Acceptance

- [ ] Replacing "report:" with G4Fixture.ReportPrefix leaves the const declaration unchanged
- [ ] Regression test

## Evidence

- const ReportPrefix = G4Fixture.ReportPrefix (CS0110) after preview. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
