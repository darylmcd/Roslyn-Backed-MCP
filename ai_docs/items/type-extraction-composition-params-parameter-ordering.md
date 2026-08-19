## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:536` — `ParameterInsertIndex()`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:576` — forwarded-argument index reuse for `this(...)` chains

## Acceptance

- A source type whose constructor ends in a `params` parameter either receives the injected composition parameter BEFORE the params array, or is refused with a topology message.
- A `this(...)` chain on such a constructor forwards the new argument at the matching ordinal without splicing into the params expansion.
- Regression test in `tests/RoslynMcp.Tests/TypeExtractionTests.cs` covering both the plain-`params` and `params`-plus-chain shapes.

## Evidence

Traced in code during PR #1281 review. `ParameterInsertIndex()` computes `IndexOf(p => p.Default is not null)`; a `params` parameter carries no `Default`, so the function returns `Parameters.Count` and `SeparatedSyntaxList.Insert` places the new required parameter after the params array, producing CS0231. The same ordinal is reused as the forwarded-argument index for `this(...)` chains.

Source: code-quality review of PR #1281 (initiative `type-extraction-composition-constructor-coverage`, sweep 20260819T180531Z).
