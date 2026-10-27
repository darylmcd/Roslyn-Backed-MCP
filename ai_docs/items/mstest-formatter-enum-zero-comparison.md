# mstest-formatter-enum-zero-comparison — Investigate enum-zero assertion formatter dependency behavior

**row:** `mstest-formatter-enum-zero-comparison` · **pri:** `Medium` · **size:** `M`

## Anchors

- `Directory.Packages.props:29`
- `Directory.Packages.props:30`
- `eng/verify-changed-format.ps1:220`
- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`

## Acceptance

- [ ] Reproduce the historical enum predicate rewrite in an isolated fixture using the currently selected dependencies; identify the responsible diagnostic, code-fix provider and package rather than attributing it by assumption.
- [ ] Compile transformed code and execute zero/nonzero flags controls for equality, inequality and assertion messages. If reproduced, adopt an upstream correction or complete repository invocation-level repair without suppressing diagnostics, skipping formatting or substituting consumer workarounds.
- [ ] Preserve formatter scope, cancellation, process ownership and failure propagation; if the current dependency no longer reproduces, record exact current evidence and close the unsupported premise.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. INVESTIGATION: current MSTest.TestAdapter/TestFramework remain 4.5.1 and the repository formatter invocation remains. Retained exact rewrite Assert.IsFalse(enumFlags != 0) to Assert.AreEqual(0, enumFlags) reportedly caused CS1503 in an absent PhysicalGraphStageTests fixture. Historical receipts do not reproduce current behavior or establish the responsible provider; dependency/invocation ownership is current and independent of Native architecture.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
