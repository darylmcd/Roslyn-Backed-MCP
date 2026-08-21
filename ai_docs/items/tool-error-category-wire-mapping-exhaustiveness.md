# tool-error-category-wire-mapping-exhaustiveness — make category mapping mechanically exhaustive

**row:** `tool-error-category-wire-mapping-exhaustiveness` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs`
- `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs`

## Acceptance

- [ ] Replace or wrap the string-only category contract with a strongly typed or table-driven representation that makes every declared category-to-wire-code decision enumerable.
- [ ] Retain the unknown-runtime-value fail-safe mapping to `InternalError` without letting a newly declared category silently use it.
- [ ] One regression enumerates the declared categories and fails when any category lacks an explicit resource wire-code mapping.

## Evidence

The classifier centralizes category strings and the resource mapper currently lists each one, but `const string` plus a discard switch arm is not compiler-exhaustive. A new or mistyped category can compile and silently receive `InternalError`, contrary to the former comments' stronger guarantee.
