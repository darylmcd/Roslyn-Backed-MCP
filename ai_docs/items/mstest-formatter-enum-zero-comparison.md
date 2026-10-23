# mstest-formatter-enum-zero-comparison — mstest-formatter-enum-zero-comparison

**row:** `mstest-formatter-enum-zero-comparison` · **pri:** `Medium` · **size:** `M` · **deps:** —

## Problem
- The repository's dependency-provided MSTest assertion code fix rewrote a valid enum flags predicate into an uncompilable mixed numeric/enum equality assertion.
- Observed input: `Assert.IsFalse((File.GetAttributes(issued) & FileAttributes.ReparsePoint) != 0, message)`.
- Observed output: `Assert.AreEqual(0, File.GetAttributes(issued) & FileAttributes.ReparsePoint, message)`.
- Correct typed assertion uses `(FileAttributes)0`; formatter must preserve contextual enum-zero conversion.
- This repository owns dependency selection and formatter invocation; upstream formatter implementation is external.

## Anchors
- `Directory.Packages.props:30` — MSTest package version 4.5.1.
- `eng/verify-changed-format.ps1:16` — repository formatter invocation.
- `tests/RoslynMcp.Tests/PhysicalGraphStageTests.cs:171` — witnessed enum flags assertion; corrected typed zero.
- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs` — existing formatter contract test family.
- Proposed new `tests/RoslynMcp.Tests/FormatterEnumAssertionContractTests.cs` — isolated code-fix compile/semantic control.

## Acceptance
- [ ] Reproduce the exact enum comparison rewrite with the installed dependency; identify the responsible diagnostic/code-fix type and package.
- [ ] Track upstream repair and adopt the corrected dependency or complete local invocation-level repair without suppressing the diagnostic or skipping formatter validation.
- [ ] Compile the transformed code and execute zero/nonzero flags controls, including both equality and inequality predicates and messages.
- [ ] Preserve existing formatter gate scope, cancellation, subprocess ownership, and failure propagation.
- [ ] Re-run the exact reproduction against the selected correction; retain old failure evidence.

## Evidence
- Session formatter206 generated mixed numeric/enum assertion; qualification210 failed CS1503 at PhysicalGraphStageTests.cs171-172 (enum and message rejected by decimal overload resolution).
- Parent corrected expected operand to `(FileAttributes)0`; qualification211 compiled.
- Logs: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/grant-format206.log`, `qualification210.log`, `qualification211.log`.
- No formatter206 before-image manifest available; responsible diagnostic ID still requires isolated reproduction rather than assumption.
