# symbol-disambiguation-agent-first-default — Make symbol candidate selection agent-first

**row:** `symbol-disambiguation-agent-first-default` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:20-137,876-985`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`

## Acceptance

- [ ] With an elicitation-capable client, multi-hit `symbol_search` returns its normal paginated candidate envelope and emits zero `elicitation/create` requests.
- [ ] Ambiguous `metadataName` calls to `find_references` and `go_to_definition` return agent-visible candidates with stable `symbolHandle` values by default and emit zero elicitation requests.
- [ ] Any retained operator picker requires explicit caller opt-in; the default is agent-first and the opt-in path preserves accept, decline, and cancel behavior.
- [ ] The agent can choose a returned `symbolHandle`, re-call the target tool, and receive the selected symbol's result without operator intervention.
- [ ] Integration tests use an elicitation-capable client and assert both the returned envelopes and exact elicitation-call counts.
- [ ] Tool descriptions and a changelog fragment document the agent-first default; follow `docs/release-policy.md` if compatibility review classifies the default flip as breaking.

## Evidence

- Codex app repro on 2026-07-20: `symbol_search("MarketDataFreshness")` returned 31 candidates, but `SymbolTools.SearchSymbols` intercepted the result and opened an operator picker before the calling agent received the candidates.
- MCP 2025-11-25 defines elicitation as information requested from the user through the client; tools and structured tool results are the model-controlled channel. See `https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation` and `https://modelcontextprotocol.io/specification/2025-11-25/server/tools`.
- Commit `58020ed3` introduced the behavior and mixes recipient terminology: `SymbolTools` says "ask the agent," while the elicitation helper and tests await user accept/decline/cancel.

## Context

`symbol_search` is a discovery/list tool whose documented response is a candidate collection. Multiple hits are a successful search result, not missing human input. The current implementation branches on `paged.Count > 1` plus the client's elicitation capability, blocks the tool call, and returns only the operator-selected symbol. Client capability indicates protocol support; it does not express operator intent.

The same audience mismatch affects ambiguous metadata-name resolution in `find_references` and `go_to_definition`: the existing structured fallback already contains stable handles sufficient for the outer agent to choose and retry.

## Notes

- Do not use MCP sampling for default disambiguation; the calling agent already has the task context needed to choose.
- Do not use `limit: 1` as a fix; it only bypasses the current gate accidentally and prevents cross-candidate comparison.
- Keep `semantic_search` out of scope; it already returns results directly and does not call elicitation.
- Treat `StructuredCallToolFilter.TryElicitChoiceAsync` as reusable operator-interaction infrastructure; this row changes when symbol tools invoke it, not the helper's general semantics.
