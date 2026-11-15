# shared-expression-requires-value-context — Respect constant-only shared-expression positions

**row:** `shared-expression-requires-value-context` · **pri:** `Medium` · **size:** `S` · **verified:** `7a04f2923bb027553154418f5e9a21a4e6bd26f4`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:726`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Reproduce matching literal arithmetic in an ordinary expression and a constant-only interpolation alignment. Refuse or preserve the alignment before token storage. Keep valid matches and compile the applied control.

## Evidence

- The preview walks matching value expressions. An alignment such as an interpolated width of 6 * 7 requires a compile-time constant, but a generated invocation is not constant. The wrong expansion predates globally qualifying helper calls.
