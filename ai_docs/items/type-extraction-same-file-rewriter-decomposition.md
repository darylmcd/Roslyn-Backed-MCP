# type-extraction-same-file-rewriter-decomposition — Isolate semantic consumer rewriting

**row:** `type-extraction-same-file-rewriter-decomposition` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (`RewriteSameFileConsumers`, `SameFileConsumerRewriter`)
- `src/RoslynMcp.Roslyn/Services/TypeExtractionSameFileConsumerRewriter.cs` (new)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Move same-file symbol discovery and syntax rewriting behind one internal collaborator so `TypeExtractionService` only orchestrates extraction stages.
- [ ] Preserve symbol-identity handling for instance, static, generic, and method-group references.
- [ ] Preserve fail-closed refusal for conditional access and constructor-time instance references.
- [ ] Add one focused regression proving extracted declarations are not rewritten while retained consumers are rewritten.

## Evidence

- The same-file correctness fix added a semantic symbol set, a syntax rewriter, and refusal policy directly inside the already broad extraction orchestrator. Keeping that independent transformation nested in `TypeExtractionService` increases the class's mixed responsibilities and makes the rewrite policy harder to test in isolation.
