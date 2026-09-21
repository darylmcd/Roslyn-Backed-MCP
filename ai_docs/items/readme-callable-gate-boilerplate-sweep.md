# readme-callable-gate-boilerplate-sweep — Correct the false "gated" boilerplate in item files

**row:** `readme-callable-gate-boilerplate-sweep` · **pri:** `Low` · **size:** `S` · **deps:** `readme-stable-callable-count-ungated`

## Anchors

- `ai_docs/items/promotion-tier-symbols.md` — representative file; sweep every `ai_docs/items/*.md` whose Gate-forced companions text cites `README.md:186`.

## Acceptance

- [ ] The false "gated by `ReadmeSurfaceCountTests`" claim about `README.md:186` is corrected (or removed) in every `ai_docs/items/*.md` carrying the boilerplate (grep: `README.md:186`), via one scripted single-pattern edit.
- [ ] That boilerplate names `src/RoslynMcp.Host.Stdio/README.md` (gated at its line 88 by `HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog`) as a second gate-forced companion, and stops describing `ReadmeSurfaceCountTests.cs` as an edit target.

## Evidence

Split from `readme-stable-callable-count-ungated` (sweep 20260921T211855Z). Once that row's test gates the `README.md:186` count, the boilerplate claim becomes true for that line but still misdescribes `ReadmeSurfaceCountTests.cs` as an edit target. About 35 item files carry it; `ai_docs/prompts/backlog-sweep-addenda.md` already has the corrected guidance.
