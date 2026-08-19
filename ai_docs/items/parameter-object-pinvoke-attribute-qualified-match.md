# parameter-object-pinvoke-attribute-qualified-match — match PInvoke attributes by qualified type

**row:** `parameter-object-pinvoke-attribute-qualified-match` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:581`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] The extern/PInvoke refusal resolves `System.Runtime.InteropServices.DllImportAttribute` / `LibraryImportAttribute` by fully-qualified name (or `Compilation.GetTypeByMetadataName`) rather than `AttributeClass.Name` string comparison.
- [ ] A test with a user-defined attribute named `DllImportAttribute` in a non-interop namespace shows the preview is NOT refused.

## Evidence

PR #1263 line 581-582 compares only `a.AttributeClass?.Name`, which is namespace-blind; any same-simple-name attribute causes a false refusal of an otherwise supported ordinary method.

## Context

Surfaced by the code-quality reviewer on `parameter-object-target-method-contract-validation` (sweep 20260818T211226Z, PR #1263) as a low-severity anti-pattern finding. Advisory — did not block that PR.
