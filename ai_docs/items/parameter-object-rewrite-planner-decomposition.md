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
