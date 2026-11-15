# shared-expression-capture-lexical-boundary — Recognize variables declared inside selected shared expressions

**row:** `shared-expression-capture-lexical-boundary` · **pri:** `Medium` · **size:** `S` · **verified:** `9fa31b98e92d93a212c875450e99734706406d90`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:941`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Use a selected expression with a lambda-owned parameter and a distinct real outer capture. Prove only the real capture becomes a helper argument, then verify preview and applied compilation and binding.

## Evidence

- CollectFreeVariables says it excludes symbols declared inside the expression. The walk accepts local and parameter symbol kinds without checking their declaring scope. A selected invocation containing a lambda can pass its parameter as an out-of-scope argument.
