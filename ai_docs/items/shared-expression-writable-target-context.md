# shared-expression-writable-target-context — Refuse writable targets in shared expression extraction

**row:** `shared-expression-writable-target-context` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs` (`IsValueExpression`, `BuildInvocation`)
- (new) `tests/RoslynMcp.Tests/ExtractSharedExpressionValueContextTests.cs`

## Acceptance

- [ ] Through the experimental preview, pair a value read of `BindingTarget.Value` with a structurally identical occurrence on the left of assignment. Refuse before token storage when the generated value-returning helper cannot be the writable target.
- [ ] Use compiler use-site semantics for the example and every candidate. Cover assignment, compound assignment and by-reference placement where the generated helper lacks the required storage. Keep read-only occurrences eligible and ensure their projected code compiles and applies.

## Evidence

- Source inspection at `403de745`. `IsValueExpression` excludes namespace and type contexts and does not check the storage required by the enclosing use. The same property reference can be a valid value and a writable target in different sites. The helper invocation supplies a value, so an original-binding comparison alone does not verify the enclosing use-site requirement.
