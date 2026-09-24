# symbol-locator-mixed-input-silently-accepted — Reject mixed or partial symbol locators

**row:** `symbol-locator-mixed-input-silently-accepted` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:48`

## Acceptance

- [ ] test_related(metadataName + filePath, no line/col) → InvalidArgument naming the conflicting locators

## Evidence

- test_related(metadataName=SampleLib.Cat, filePath=…) accepted; metadataName wins. Related test-only row symbollocatorfactory-drift-tool-test-gap. — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`
