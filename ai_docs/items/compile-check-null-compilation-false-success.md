# compile-check-null-compilation-false-success

**row:** `compile-check-null-compilation-false-success` · **pri:** `Medium` · **size:** `S` · **deps:** —

## Anchors

- src/RoslynMcp.Roslyn/Services/CompileCheckService.cs
- tests/RoslynMcp.Tests/CompileCheckServiceTests.cs

## Acceptance

- [ ] A selected unsupported/no-compilation project must report an incomplete non-success verdict instead of counting as evaluated. Preserve cancellation and successful C# project behavior.

## Evidence

CollectDiagnosticsAsync increments CompletedProjects when SourceGeneratorCompilation.CreateAsync returns null, allowing Success with no evaluated compilation.
Observed during direct remediation on 2026-09-14.
