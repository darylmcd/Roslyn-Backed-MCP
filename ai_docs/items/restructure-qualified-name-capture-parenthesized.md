# restructure-qualified-name-capture-parenthesized — Treat qualified-name captures as primary in restructure_preview

**row:** `restructure-qualified-name-capture-parenthesized` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestructureService.cs:443`
- `tests/RoslynMcp.Tests/RestructureServiceTests.cs`

## Acceptance

- [ ] A capture of `global::System` (AliasQualifiedName) or `A.B` (QualifiedName) spliced into a member-access receiver slot is not wrapped in parentheses and the result compiles.
- [ ] Regression test covers both name forms in a receiver slot.

## Evidence

- HEAD `RestructureService.cs:443` `IsPrimaryExpression` lists `IdentifierNameSyntax or GenericNameSyntax or PredefinedTypeSyntax` but not `QualifiedNameSyntax` / `AliasQualifiedNameSyntax`, so `__a__.Console.WriteLine()` with `__a__ = global::System` becomes `(global::System).Console`. Traced by the cold review of PR #1615 (code read).

## Context

Spin-off from `restructure-preview-splice-without-parenthesization` (PR #1615), which introduced the parenthesization.
