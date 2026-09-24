# fix-all-analyzer-assembly-provider-not-found — Load fix-all providers from analyzer references by full path

**row:** `fix-all-analyzer-assembly-provider-not-found` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:334`

## Acceptance

- [ ] fix_all_preview(diagnosticId=CA1822) finds MarkMembersAsStaticCodeFix (code_fix_preview already does)
- [ ] Regression test with an analyzer-package diagnostic

## Evidence

- fix_all_preview CA1822/MSTEST0046 → 'No code fix provider' while code_fix_preview on the same diagnostic succeeds. AnalyzerFileReference.Display is a simple name, not a path. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
