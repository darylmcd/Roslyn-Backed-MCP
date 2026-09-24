# argument-exception-throw-sites-lack-public-message — Throw PublicArgumentException with a parameter name from ParameterValidation

**row:** `argument-exception-throw-sites-lack-public-message` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ParameterValidation.cs:60`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:43`
- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:154`
- `src/RoslynMcp.Host.Stdio/Tools/ScriptingTools.cs:33`

## Acceptance

- [ ] symbol_search{limit:-3} → message names 'limit' and the valid range
- [ ] analyze_snippet{kind:'bogusKind'} names the valid kinds
- [ ] evaluate_csharp{timeoutSeconds:-1} states 'must be greater than 0'
- [ ] An analyzer or test guards against new plain `throw new ArgumentException(msg)` without paramName in Host.Stdio tool paths

## Evidence

- symbol_search{limit:-3} → "Parameter '<unknown>' is invalid…"; analyze_snippet bogus kind same; ValidatePagination (17 callers) throws paramless ArgumentException; 18 single-line paramless throws repo-wide. go_to_definition(line=99999)/enclosing_symbol(line=0) → "Parameter 'line' is invalid…" without the file line count. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- ToolErrorHandler.BuildSafeArgumentMessage intentionally redacts raw ArgumentException messages (they may echo inputs); only PublicArgumentException passes through. The fix is at the throw sites, not in the redaction layer.
