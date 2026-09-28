# argument-exception-throw-sites-lack-public-message — Throw PublicArgumentException with a parameter name from ParameterValidation

**row:** `argument-exception-throw-sites-lack-public-message` · **pri:** `Medium` · **size:** `L` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ParameterValidation.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/ParameterValidationTests.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`
- `tests/RoslynMcp.Tests/FindReflectionUsagesPaginationTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] symbol_search{limit:-3} names 'limit' and the valid range through the envelope.
- [ ] Enum validators list valid values and never echo raw input; ValidatePagination reports caller-renamed pairs (CallerArgumentExpression).
- [ ] Every ArgumentException construction in the 2 files is PublicArgumentException.

## Evidence

- symbol_search{limit:-3} → "Parameter '<unknown>' is invalid…"; analyze_snippet bogus kind same; ValidatePagination (17 callers) throws paramless ArgumentException; 18 single-line paramless throws repo-wide. go_to_definition(line=99999)/enclosing_symbol(line=0) → "Parameter 'line' is invalid…" without the file line count. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- ToolErrorHandler.BuildSafeArgumentMessage intentionally redacts raw ArgumentException messages (they may echo inputs); only PublicArgumentException passes through. The fix is at the throw sites, not in the redaction layer.
2026-09-26: analyze_snippet bogus-kind acceptance moved to public-argument-exception-core-move (needs PublicArgumentException in Core).
2026-09-26: re-scoped as a child of the argument-error contract redesign.

Over the Rule 3/4 target by a named forcing shape: gate-forced-companion (tests, 4 > 3). Gate: the MSTest suite. ThrowsExactly<ArgumentException> fails deterministically once ValidatePagination/SymbolTools throw the PublicArgumentException subclass. Pins: SymbolSearchPaginationTests.cs:179,:195; FindReflectionUsagesPaginationTests.cs:344,:356; ExpandedSurfaceIntegrationTests.cs:349,:368,:385; ParameterValidationTests.cs:24-106. ExpandedSurfaceIntegrationTests.cs is shared with code-action-and-flow-argument-refusals-public-message (disjoint pins :632-:694).

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
