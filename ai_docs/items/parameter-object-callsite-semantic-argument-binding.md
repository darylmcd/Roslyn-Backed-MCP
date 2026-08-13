# parameter-object-callsite-semantic-argument-binding — Bind parameter-object call arguments semantically

**row:** `parameter-object-callsite-semantic-argument-binding` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`CollectCallerSpansAsync`, `BuildRewrittenArgumentList`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Derive every source argument-to-parameter association from Roslyn invocation operations instead of lexical argument indexes.
- [ ] Preserve in-position named arguments followed by positional arguments, reduced-extension receivers, and every trailing ungrouped `params` argument.
- [ ] Refuse any invocation shape that cannot be mapped completely before storing a preview token.
- [ ] Add one table-driven apply regression covering those three legal call shapes with equal pre/post compile errors.

## Evidence

- The current syntax-only slot array does not advance after an in-position named argument, treats a reduced extension receiver as an explicit argument, and retains only one trailing variadic argument.

## Acceptance amendment (2026-08-13 adversarial review)

- Preserve the source program's left-to-right argument evaluation order, including named arguments supplied out of parameter order and interleaved grouped/retained arguments; otherwise refuse before preview storage.
- Resolve and verify the target through `IInvocationOperation`; inventory or refuse non-invocation references and method-group uses.
- Make the regression observe a runtime trace, not only equal compiler-error counts.
