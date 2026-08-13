# caching-hints-tools-list — Caching hints + deterministic tools/list ordering

**row:** `caching-hints-tools-list` · **pri:** `Low` · **size:** `S` · **deps:** `sdk-2x-upgrade`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs` (or a small list-result filter) — `ttlMs`/`cacheScope` population + ordering guarantee
- `tests/RoslynMcp.Tests/` (ordering stability + hint presence)

## Acceptance

- [ ] tools/list returns tools in a deterministic, stable order across processes (enables client prompt-cache hits on the ~55k-token surface)
- [ ] `ttlMs`/`cacheScope` (SEP-2549 CacheableResult) populated on tools/list, prompts/list, resources/list and other static list results
- [ ] Wire probe confirms both

## Evidence

- 2026-07-28 spec requires CacheableResult fields on list/read results and recommends deterministic ordering; a second lever on session cost for clients that cache — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §4, §5

## Notes

- Blocked on `sdk-2x-upgrade` (CacheableResult types ship in SDK 2.x).
