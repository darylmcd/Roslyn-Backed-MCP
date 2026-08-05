# recommend-workflow-missing-semantic-grep-route — add a pattern-search routing branch to recommend_workflow

**row:** `recommend-workflow-missing-semantic-grep-route` · **pri:** `Medium` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkflowRecommendationTools.cs:33-95` (`Recommend` — exactly 5 `ContainsAny` branches: caller/references/usages, outline, compile, related-test, rename; falls through to `discover_capabilities` for everything else, including pattern-search-shaped tasks)
- tests under `tests/RoslynMcp.Tests/Tools/WorkflowRecommendationToolsTests.cs` (or equivalent)

## Acceptance

- [ ] A new `ContainsAny` branch routes pattern/text-search-shaped phrasing ("find usages of pattern", "search the codebase for", "sensitive API", "sync-over-async", or similar) to `PrimaryTools: ["semantic_grep"]` with `Avoid: ["rg", "grep", "manual file scan"]`
- [ ] Existing 5 branches unchanged
- [ ] Regression test: a task string like "find all usages of the GetAwaiter().GetResult() sync-over-async pattern" routes to `semantic_grep`, not the `discover_capabilities` fallback

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2b grep-over-semantic-search gap row + §3 pattern 6. 3 sessions, confirmed identical in BOTH harnesses (1 claude: `55209271`, 2 codex: `019faec2`, `019fae3e`) — agents defaulted to `grep`/`rg` for a sensitive-API regex sweep, a sync-over-async pattern search, and a constructor-call-site census, even in sessions actively using other Roslyn tools moments before/after. `recommend_workflow` was never invoked in any of the 3.

## Context

Source verification (2026-08-05, this row's authoring pass) confirmed `recommend_workflow` already has solid keyword coverage for symbol-identity lookups ("caller", "references", "usages", "used by", "who calls" → `find_references`, with `Avoid: ["rg", "grep"]` already present). The gap is narrower than the source report's original framing ("add grep-shaped trigger phrasing to tool descriptions") — the phrasing already exists for the references/usages shape. What's genuinely missing is any routing branch at all for PATTERN-search-shaped tasks (a multi-candidate regex sweep, a specific code idiom like `GetAwaiter().GetResult()`) — these are not symbol lookups (`find_references` can't do them), they're `semantic_grep`'s actual domain, and `semantic_grep` is never mentioned anywhere in `WorkflowRecommendationTools.cs`. Two of the three retro sessions' grep usages were exactly this pattern-search shape, not the symbol-lookup shape `recommend_workflow` already covers.

## Notes

This does not address the deeper "recommend_workflow itself was never invoked" discoverability question raised in the retro — that's the pre-existing, broader `initiative-executor-roslyn-tool-discovery-experiment` (Low) and `roslyn-mcp-cross-repo-steering-gap` (Defer) rows already in this backlog. This row is scoped narrowly to the concrete routing-table gap the source-code check surfaced.
