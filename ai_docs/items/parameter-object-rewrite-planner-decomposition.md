# parameter-object-rewrite-planner-decomposition — Decompose parameter-object rewrite planning

**row:** `parameter-object-rewrite-planner-decomposition` · **pri:** `Low` · **size:** `M` · **deps:** `parameter-object-callsite-semantic-argument-binding,parameter-object-target-method-contract-validation,parameter-object-value-type-mutation-semantics,parameter-object-declaration-metadata-preservation,parameter-object-generic-dto-type-validity,parameter-object-dto-reference-qualification,parameter-object-dto-output-boundary-validation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`RewriteCallSitesAsync`, `BuildRewrittenArgumentList`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Extract semantic argument-slot projection from syntax emission without changing positional, named, default, or trivia behavior.
- [ ] Extract document-level caller mapping/rewrite planning from solution mutation.
- [ ] Preserve one atomic preview and callsite counts.
- [ ] Add one nested or recursive same-document apply regression proving declaration/body expansion cannot redirect a caller rewrite.

## Evidence

- Adjacent remediation measured `BuildRewrittenArgumentList` at CC18/98 LOC and `RewriteCallSitesAsync` at CC14/77 LOC; both mix independent semantic and mutation concerns.

## Acceptance amendment (2026-08-13 adversarial review)

- Add cancellation checkpoints across caller, span, and syntax-path planning loops while decomposing the service.

## Amendment — 2026-08-18 (PR #1265 code-quality review)

Two additional defects traced in the `parameter-object-callsite-semantic-argument-binding` diff, both in this row's existing anchor set. Fold into the decomposition rather than filing sibling rows.

### Additional anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:830` (`EnforceEvaluationOrderPreserved`)
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1200` (`RewriteCallSitesAsync`)
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1285` (`BuildRewrittenArgumentList`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:1030` (`ReducedExtensionReceiver_CallSite_BindsWithoutSlotShift`)

### Additional acceptance

- [ ] Both the evaluation-order gate and the argument emitter derive the emitted order from ONE shared helper, so a change to emission cannot silently desync the gate. (Today `EnforceEvaluationOrderPreserved` and `BuildRewrittenArgumentList` each independently compute DTO-at-min-grouped-ordinal + retained-in-ordinal-order + variadic-last, ~455 lines apart.)
- [ ] An unresolvable syntax path or a null rewritten argument list REFUSES the preview instead of silently leaving a call site unrewritten — or the atomicity claim in the `CollectCallSiteBindingsAsync` doc comment is narrowed to the collection phase only.
- [ ] `ReducedExtensionReceiver_CallSite_BindsWithoutSlotShift` gains the `ApplyWithVerify` + `preErrorCount == postErrorCount` tail its sibling tests already carry — it currently asserts only the unified diff, leaving the `ordinalOffset` arithmetic (the subtlest new logic) without a compile-verified apply.
- [ ] The `Unmappable(site)` message at `:760` describes all three refusal causes (unsupported params/collection expansion, out-of-range computed ordinal, static-form call to a reduced extension method), not just the first.

### Evidence

`RewriteCallSitesAsync`'s `targets` dictionary omits any binding whose `ResolveSyntaxPath` returns a non-`InvocationExpressionSyntax`, and `BuildRewrittenArgumentList` returns `null` on the variadic branch — both skip without raising, contradicting the "covers every reference atomically" comment PR #1265 added. Severity assessed medium by the reviewer (advisory; did not block that PR).
