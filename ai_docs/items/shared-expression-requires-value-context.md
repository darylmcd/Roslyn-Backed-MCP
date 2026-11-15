# shared-expression-requires-value-context — Respect constant-only shared-expression positions

**row:** `shared-expression-requires-value-context` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs` (`IsValueExpression`, `CollectMatchingExpressionHitsAsync`, `BuildInvocation`)
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Reproduce matching literal arithmetic in an ordinary expression and a constant-only interpolation alignment. Refuse or preserve the alignment before token storage. Keep valid matches and compile the applied control.
- [ ] Select only `6 * 7` in repeated `Bind((6 * 7) + 1)` calls. Preserve the surrounding constant conversion and overload binding, or refuse before a token.

## Evidence

- The preview walks matching value expressions. An alignment such as an interpolated width of 6 * 7 requires a compile-time constant, but a generated invocation is not constant. The wrong expansion predates globally qualifying helper calls.

## Notes

Cold plan review identified a compiling call-site variant of the constant-context loss. With `Bind(byte)` and `Bind(long)`, replacing only `6 * 7` inside `Bind((6 * 7) + 1)` by an int-returning helper can stop the enclosing sum from converting to byte and select the long overload while compiling. Verify surrounding binding in addition to required-constant syntax. This is separate from the overload loss in a helper body after converting a const capture to a parameter.
