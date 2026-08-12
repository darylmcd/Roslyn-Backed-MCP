# analyzer-test-reference-cache-parallelism-race — Serialize analyzer harness reference-assembly extraction

**row:** `analyzer-test-reference-cache-parallelism-race` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ServerSurfaceCatalogAnalyzerTests.cs`
- `tests/RoslynMcp.Tests/SliceFieldDetectionTests.cs`
- `tests/RoslynMcp.Tests/StdoutWriteAnalyzerTests.cs`

## Acceptance

- [ ] The three analyzer-harness classes cannot execute concurrently against Microsoft.CodeAnalysis.Testing's process-global `test-packages` extraction cache.
- [ ] A fresh-cache targeted run of all three classes passes without `ArgumentNullException` or `DirectoryNotFoundException` from `ReferenceAssemblies.ResolveCoreAsync`.

## Evidence

- PR #1221 self-hosted CI failed 19 analyzer tests after concurrent first-use extraction left `Microsoft.NETCore.App.Ref.8.0.0` absent under `C:\Windows\SystemTemp\test-packages`.

## Context

Only the three named test classes use `ReferenceAssemblies.Net.Net80`. Preserve class-level parallelism for the rest of the 1,933-test assembly; serialize only these cache-sharing classes.
