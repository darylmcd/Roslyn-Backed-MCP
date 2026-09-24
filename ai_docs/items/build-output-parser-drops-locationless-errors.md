# build-output-parser-drops-locationless-errors — Parse location-less MSBuild/NuGet errors in DotnetOutputParser

**row:** `build-output-parser-drops-locationless-errors` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:12`

## Acceptance

- [ ] build_project on a project failing with NU1201 reports errorCount ≥1 with the diagnostic id
- [ ] Regression test with a captured NU1201 output line

## Evidence

- build_project(SampleLib.Tests) → exitCode 1, stdout 'SampleLib.Tests.csproj : error NU1201: …', errorCount 0, diagnostics []. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
