# validation-tools-error-envelope-not-iserror — Set isError on build/test tool failure envelopes

**row:** `validation-tools-error-envelope-not-iserror` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:454`

## Acceptance

- [ ] test_related / build_workspace / build_project / test_discover / test_run / test_related_files error paths return isError=true
- [ ] Regression test asserts isError on one induced failure per tool family

## Evidence

- test_related(filePath,line) → {"error":true,"category":"InvalidArgument"…} delivered with isError=false; symbol_info same error → isError=true. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
