# list-tool-response-byte-budget — serialized-byte ceiling for `find_references`

**row:** `list-tool-response-byte-budget` · **pri:** `Medium` · **size:** `S` · **deps:** `—` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/FindReferencesSummaryTests.cs`

## Acceptance

- [ ] `find_references` enforces a serialized-**byte** ceiling on its response and stops adding items once the serialized JSON would cross it. Default ~32,000 bytes — below the 58,997-character payload Claude rejected — and overridable by an env var following the `ROSLYNMCP_MAX_RELATED_FILES` / `ROSLYNMCP_PREVIEW_MAX_ENTRIES` precedent. The ceiling and its default are expressed in bytes, never tokens.
- [ ] No new tool parameter is added: the ceiling is env-configured, so the `tools/list` schema payload is unchanged (the open `method-diet-*` / `param-dedupe-*` rows are actively shrinking it).
- [ ] The response gains an **additive** `nextOffset` field naming the first omitted reference. The existing `offset` field keeps its current meaning — the echo of the request offset — matching every other list tool (`AnalysisTools.cs`, `AdvancedAnalysisTools.cs`, `AnalyzerInfoTools.cs`). No existing field changes meaning.
- [ ] `hasMore` is derived from the count of items actually emitted after the ceiling truncates, not from the pre-ceiling `Skip(offset).Take(limit)` list. A ceiling-shortened page reports `hasMore: true`.
- [ ] Remaining shape preserved: `count`, `totalCount`, `hasMore`, `offset`, `limit`, `summary`, `items`; `totalCount` still reports the unbounded total.
- [ ] A caller-supplied `limit` smaller than the byte ceiling still governs; the ceiling only ever shortens a page, never lengthens it.
- [ ] Regression on a high-fan-out symbol, exercised in **both** `summary: false` and `summary: true` (the quoted failure [Q2a] was `summary:true, limit:200`): each page is byte-bounded, `hasMore` is true, and paging by the returned `nextOffset` enumerates every reference exactly once — none skipped, none duplicated.

## Evidence

- Row-bounded list tools bound row count but not bytes, so a `limit=100`/`limit=200` page can still exceed both harnesses' result caps — Claude fails the call and spills the payload to disk (58,997 and 147,962 characters quoted), Codex silently returns a cut-off head ("Warning: truncated output (original token count: 12808)"), which can read as a complete result. Server payloads were well-formed; only the `find_references` slice of the proposed server-side enhancement is in scope here.
- Source: `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`, finding `list-tool-response-byte-budget` (§4.2).

## Context

Paging and serialization for `find_references` both live at the tool endpoint — `SymbolTools.FindReferences` does `results.Skip(offset).Take(limit)` then `JsonSerializer.Serialize` (`SymbolTools.cs:302-315`) — not in `ReferenceService`, so the ceiling belongs at that serialization site. `offset` is already a public parameter, so no schema break is required.

The retro's line cites (`SymbolTools.cs:270`/`:271`) resolve to the parameter declarations; the implementation point is the serialization block where `Skip`/`Take`, `hasMore`, and `JsonSerializer.Serialize` sit together. `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` exists but has zero hits for `limit`/`offset`/`hasMore` — it materializes the full list and does no paging or serialization, so it is deliberately not an anchor.

The retro also names `test_related_files`, `get_coupling_metrics`, `find_unused_symbols`, and `find_dead_fields` as candidates. Out of scope for this row: `find_references` is the only tool with a quoted truncation in both harnesses, and adopting the ceiling elsewhere should be separate rows once the shape is proven here. `ValidationTools.cs`, `CouplingAnalysisTools.cs`, and `AdvancedAnalysisTools.cs` were all verified present, so those follow-ons will have live anchors.

The client-side halves (Codex exec-output cap, Claude result wrapper) are harness defects and are not actionable in this repo.
