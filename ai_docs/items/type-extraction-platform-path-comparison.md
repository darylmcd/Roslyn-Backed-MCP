# type-extraction-platform-path-comparison — Use platform path identity for extraction consumers

**row:** `type-extraction-platform-path-comparison` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `src/RoslynMcp.Roslyn/Helpers/FileSystemPath.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Canonicalize source and reference file paths and compare with `FileSystemPath.Comparison`.
- [ ] On case-sensitive systems, distinct files differing only by case remain external consumers.
- [ ] On Windows, equivalent casing variants identify the same file.
- [ ] Preserve all other external-consumer refusal behavior in one platform-aware regression shape.

## Evidence

- `TypeExtractionService` hard-codes `OrdinalIgnoreCase` for source/reference identity despite the existing platform abstraction.
