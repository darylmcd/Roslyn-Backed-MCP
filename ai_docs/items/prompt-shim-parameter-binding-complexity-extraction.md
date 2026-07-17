# prompt-shim-parameter-binding-complexity-extraction — Simplify prompt parameter binding

**row:** `prompt-shim-parameter-binding-complexity-extraction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:22-220`
- `tests/RoslynMcp.Tests/PromptShimToolsTests.cs` (new)
- `tests/RoslynMcp.Tests/PromptSmokeTests.cs`

## Acceptance

- [ ] Replace the no-op async binder with a synchronous `BuildParameterValues` and focused parse, required-parameter, service/default, and deserialization helpers.
- [ ] The binder and every new or changed helper measure cyclomatic complexity at or below 8.
- [ ] Preserve precedence: cancellation token, DI service, supplied JSON, default value, then required-parameter error.
- [ ] Preserve malformed/non-object/type-mismatch/missing wording and `JsonDocument` disposal.
- [ ] A direct success regression pins the `dead_code_audit` prompt's workspace, cancellation token, null project default, prompt name, two-parameter schema, and user text.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `BuildParameterValuesAsync` at CC 11; its only await is `Task.CompletedTask`.

## Dependencies

- None.
