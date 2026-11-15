# shared-expression-dead-helper-insertion-fallback — Consolidate shared helper insertion

**row:** `shared-expression-dead-helper-insertion-fallback` · **pri:** `Low` · **size:** `S` · **verified:** `7a04f2923bb027553154418f5e9a21a4e6bd26f4`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:674`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Establish whether any valid request can reach helper fallback without rewriting the selected example. Preserve exact insertion and edited preview output in the reaching path or remove duplication after proving it cannot reach storage.

## Evidence

- The phase-four fallback is documented as unreachable and repeats formatting and diff generation. Exact declaration insertion for ordinary hits is independently exercised by the namespace regression.
