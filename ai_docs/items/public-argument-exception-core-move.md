# public-argument-exception-core-move — Move PublicArgumentException to Core and publish Roslyn-layer argument refusals

**row:** `public-argument-exception-core-move` · **pri:** `Medium` · **size:** `M` · **deps:** `argument-exception-throw-sites-lack-public-message`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:15`
- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:154`
- `src/RoslynMcp.Core/Services/PublicInvalidOperationException.cs`

## Acceptance

- [ ] `PublicArgumentException` lives in `RoslynMcp.Core.Services` (mirroring `PublicInvalidOperationException`), so RoslynMcp.Roslyn throw sites can use it.
- [ ] analyze_snippet{kind:'bogusKind'} names the valid kinds (SnippetAnalysisService.cs:154) instead of "Parameter '<unknown>' is invalid".
- [ ] go_to_definition(line=99999) / enclosing_symbol(line=0) name the parameter and the file's line bound.
- [ ] Regression tests assert both messages through the tool error envelope.

## Evidence

- analyze_snippet bogus kind → "Parameter '<unknown>' is invalid…"; go_to_definition(line=99999)/enclosing_symbol(line=0) → "Parameter 'line' is invalid…" without the file line count. — mcp-surface-audit 20260924-1305 (check C1).
- `PublicArgumentException` is `internal` to Host.Stdio (`ToolErrorHandler.cs:15`); RoslynMcp.Roslyn cannot reference the host (plan-deepener, backlog-remediate 20260926T234932Z).

## Context

- Split from `argument-exception-throw-sites-lack-public-message` (acceptance bullet 2) during backlog-remediate 20260926T234932Z; that row keeps the Host.Stdio throw sites and the banned-API guard. Host.Stdio users of the type (`ClientRootPathValidator`, `LspSourceLocationArgumentNormalizer`, `ReferenceResponsePager`) need a `using` after the move.
