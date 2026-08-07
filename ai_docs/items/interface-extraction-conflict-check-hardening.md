# interface-extraction-conflict-check-hardening — cover the extract-interface conflict check and stop swallowing its failure signal

**row:** `interface-extraction-conflict-check-hardening` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:393-421`
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs`

## Acceptance

- [ ] A test pre-creates a type with the same name the extraction would produce and asserts `PreviewExtractInterfaceAsync` throws `InvalidOperationException` with the "already exists in project" / "Choose a different interface name" message — proves the `ICompilationCache`-routed conflict check (line 407) still detects conflicts.
- [ ] The per-project catch at `InterfaceExtractionService.cs:416-419` logs at Warning (not Debug) and narrows from bare `Exception` to the compilation-retrieval exceptions actually expected, so a cache/compilation failure no longer silently degrades the conflict check to a no-op.

## Evidence

- Traced during code-quality review of PR #1179 (`compilation-cache-adoption-read-side`): `rg` across `tests/` for the conflict-check's exact error strings returns zero hits, and the catch unconditionally swallows non-cancellation exceptions from the newly cache-routed line into `LogDebug`.

## Context

Spin-off from routing `InterfaceExtractionService`'s conflict check through `ICompilationCache` (PR #1179). The row's remaining `compilation-cache-adoption-read-side` scope (group-c helpers: `SymbolResolver`, `SymbolHandleSerializer`) does not cover this file, so this is a separate row rather than an amendment.
