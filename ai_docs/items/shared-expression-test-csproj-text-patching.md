# shared-expression-test-csproj-text-patching — Edit test csproj files structurally

**row:** `shared-expression-test-csproj-text-patching` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/ExtractSharedExpressionValueContextTests.cs`:597-598

## Acceptance

- [ ] The property is added through XML (XDocument) or a shared fixture helper, and the test asserts the property is present before relying on it.

## Evidence

- shared-expr report (PR 2142): "WithWorkspaceAsync csproj text patching is brittle string replacement." Verified at 12bc2c8b: lines 597-598 `File.ReadAllTextAsync(project)).Replace(`.
