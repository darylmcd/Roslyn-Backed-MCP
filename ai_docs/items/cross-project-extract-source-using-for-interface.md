# cross-project-extract-source-using-for-interface — Add the using for a cross-project interface to the source file

**row:** `cross-project-extract-source-using-for-interface` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`PreviewExtractInterfaceAsync`, `CreateInterfaceExtractionSolutionAsync` — both call `InterfaceBaseTypeWriter.AddBaseType` with a simple name only)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Extracting into another project whose namespace differs from the source type's namespace adds `using <interface namespace>;` (or a qualified base) to the source file, so the source compiles; red-first test with source in a different namespace than the receiving project.
- [ ] Same-namespace and in-project controls keep producing no using.

## Evidence

- Observed 2026-10-28 while building `InterfaceMemberProjectorTests`: source in `MatrixSource` extracted to `Contracts` left `MatrixService.cs` with CS0246 `IMatrixService`; the test now puts the source in the receiving namespace to avoid it.
