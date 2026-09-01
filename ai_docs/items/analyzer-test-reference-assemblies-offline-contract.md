# analyzer-test-reference-assemblies-offline-contract — Make analyzer tests restore-complete

**row:** `analyzer-test-reference-assemblies-offline-contract` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ServerSurfaceCatalogAnalyzerTests.cs:295`
- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj`

## Acceptance

- [ ] Analyzer tests perform no package download or network access during `dotnet test` after the repository restore phase completes.
- [ ] Preserve the intended .NET 8 reference-assembly semantics for every `ServerSurfaceCatalogAnalyzerTests` case.
- [ ] Prove the analyzer suite passes with NuGet sources unavailable after a clean, successful restore.

## Evidence

- The 2026-09-01 required `just ci` gate restored successfully, then `NoDiagnostics_WhenCatalogAndAttributesAgreeAcrossAllKinds` failed while `ReferenceAssemblies.Net.Net80` lazily downloaded `Microsoft.NETCore.App.Ref.8.0.0` from NuGet; TRX recorded a DNS failure after 2,788 other tests passed.

## Context

Keep the fix in test infrastructure. Do not weaken the analyzer assertions or classify external network failure as a passing test.
