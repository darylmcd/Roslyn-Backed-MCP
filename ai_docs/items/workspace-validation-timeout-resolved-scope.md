# workspace-validation-timeout-resolved-scope

**row:** `workspace-validation-timeout-resolved-scope` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs`

## Acceptance

- [ ] Use the scope actually resolved inside ValidateInternalAsync when emitting timeout DTOs. Cover tracker-derived scope and explicit unknown paths through both entry points; do not claim an empty scope was validated after a nonempty tracker set was resolved.

## Evidence

2026-09-15 direct validation-verdict review: ValidateAsync catches outside scope resolution and passes raw changedFilePaths or an empty array; the Git fallback timeout catch also passes an empty array. CreateTimeoutResult always discards UnknownFilePaths.
