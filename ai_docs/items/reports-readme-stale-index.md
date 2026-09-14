/* verified: do_text prose = 224 chars excl. tags (under 250); 1 anchor bullet, 1 backticked path, size S consistent; no duplicate across 183 actionable rows or ai_docs/items/*.md; not shipped (last touch to this file is f49516c9, the commit that introduced the claim; zero changelog.d/ fragments and zero CHANGELOG.md hits). */

# reports-readme-stale-index — Stop the reports index asserting an empty directory

**row:** `reports-readme-stale-index` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/reports/README.md`

## Acceptance

- [ ] `ai_docs/reports/README.md` no longer claims "No standalone report artifacts are currently retained" while files exist in the directory.
- [ ] The `## Current files` section is replaced by a non-enumerative statement of the supersession-gated retention rule this directory actually runs under — kept until a newer report names the same target via a `supersedes:` header line or explicit citation; retirement is approval-required and never automatic — so the section stays true as reports land.
- [ ] The new wording does not restate a latest-3 rotation cap; that cap governs `../audit-reports/` and `ai_docs/audits/`, not this directory.
- [ ] The May 2026 intake narrative is preserved as dated history rather than a current-state claim, or dropped if it carries no remaining value.
- [ ] No file count, file list, or other enumerable fact about the directory survives in the README, so the section cannot go stale on the next report landing.

## Evidence

`ai_docs/reports/README.md:42-44` states that no report artifacts are retained; the directory currently holds six reports (2026-05 and 2026-06 retros, a 2026-07 refactor-matrix pass, a 2026-08 retro, a 2026-08 token-overhead audit, and the 2026-09-13 retro), so an agent trusting the index concludes the backlog-intake source files do not exist. The root cause is that the section enumerates directory contents at all — any factual snapshot there goes stale on the next landing; stating the retention rule instead is self-maintaining.

- Report: `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`
- Finding id: `reports-readme-stale-index`
