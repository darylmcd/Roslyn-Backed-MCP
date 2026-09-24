# replace-invocation-rewrites-replacement-body — Exclude the replacement method's own body from replace_invocation_preview

**row:** `replace-invocation-rewrites-replacement-body` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:235`

## Acceptance

- [ ] Replacing Summarize(...) with SummarizeV2(...) where SummarizeV2 delegates to Summarize leaves SummarizeV2's body unchanged
- [ ] Regression test asserts no callsite inside the replacement symbol is rewritten

## Evidence

- replace_invocation_preview → SummarizeV2 body becomes SummarizeV2(verbose,title,count): infinite recursion, 0 diagnostics (reverted). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
