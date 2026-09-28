# catalog-internal-argument-guards-not-caller-errors — Catalog/schema-index/structured-result internal invariants stop throwing ArgumentException/AOORE

**row:** `catalog-internal-argument-guards-not-caller-errors` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolOutputSchemaIndex.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs`
- `src/RoslynMcp.Host.Stdio/Tools/StructuredToolResult.cs`
- `tests/RoslynMcp.Tests/Batch1OutputSchemaTests.cs`

## Acceptance

- [ ] No ArgumentException/AOORE construction remains in the 3 files.
- [ ] Union-declaration misuse throws InvalidOperationException (or ThrowIf helpers); impossible arms throw UnreachableException.

## Evidence

- 6 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~25000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
