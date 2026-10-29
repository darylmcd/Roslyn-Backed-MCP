# generated-interface-alias-copy-duplicates-cs1537 — Stop copying source global alias usings into same-project generated interface files (CS1537 duplicate alias)

**row:** `generated-interface-alias-copy-duplicates-cs1537` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (PreserveSpecialAndRequiredSourceUsings)
- `tests/RoslynMcp.Tests/GeneratedInterfaceGlobalUsingOrderIntegrationTests.cs`

## Acceptance

- [ ] Standalone extract_interface from a class in a project whose source has `global using Alias = ...;` produces a compiling file (no CS1537), keeping alias resolution intact.
- [ ] Standalone integration test covers global alias beside global plain/static and ordinary controls; cross-project behavior unchanged.

## Evidence

- generated-interface-global-using-order implementation: copying a same-project global alias into the generated file yields CS1537 regardless of order; standalone test could cover only global plain/static. Global plain/static copied into a same-project file also yield hidden CS8933.
