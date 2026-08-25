# shipped-skills-hardcode-bare-roslyn-tool-prefix — make consumer-facing artifacts prefix-agnostic

**row:** `shipped-skills-hardcode-bare-roslyn-tool-prefix` · **pri:** `Medium` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `skills/mcp-server-surface-test/SKILL.md:17,20,30,170`
- `skills/analyze/SKILL.md:23,25`
- `.claude-plugin/mcp.json` (server key `roslyn`)
- `.claude-plugin/plugin.json` (plugin name `roslyn-mcp`)
- `eng/verify-skills-are-generic.ps1`
- `ai_docs/prompts/roslyn-mcp-multisession-retro.md`

## Acceptance

- [ ] **VERIFY FIRST** whether agents resolve roslyn tools by suffix despite the bare-prefixed instruction (drives severity → bump to Medium/High if the surface-test entry gate misfires for plugin consumers)
- [ ] Consumer-facing artifacts made prefix-agnostic (refer to tools by suffix e.g. `server_info`; note both prefix forms); usage-audit tooling matches BOTH prefixes
- [ ] Genericity guard `eng/verify-skills-are-generic.ps1` extended with a prefix-agnostic assertion + sweep, not ad-hoc edits (heroic-risk: ~20 shipped skill files)
- [ ] Regression: genericity test asserts shipped `skills/**` reference roslyn tools prefix-agnostically (no bare `mcp__roslyn__` literal inside a "verify it appears in your tool surface" instruction)

## Evidence

- `skills/mcp-server-surface-test/SKILL.md:17` literal verify-step + 2026-06-08 retro measurement error; `ai_docs/reports/20260608T203050Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` Correction block. Source: 2026-06-08 retro follow-up.

## Context

Shipped skills + usage-audit tooling hard-code the bare `mcp__roslyn__` tool prefix, but the canonical consumer install (marketplace plugin `roslyn-mcp`) surfaces the namespaced prefix `mcp__plugin_roslyn-mcp_roslyn__`. The prefix is **client-assigned from the registration path** (this repo's `.mcp.json` registers a bare `roslyn` server key for the dev build → `mcp__roslyn__*`; the plugin registers the same server → `mcp__plugin_{plugin}_{server}__` = `mcp__plugin_roslyn-mcp_roslyn__*`), so it is NOT fixable in server code.

Consumer-facing risk: `skills/mcp-server-surface-test/SKILL.md:17` instructs *"Verify `mcp__roslyn__server_info` appears in your current tool surface and call it"* and **halts** if absent (`:18`/`:170`) — for a plugin install the literal name differs, so the verify-step may misfire the hard-stop for legitimate users. The same gap made the 2026-06-08 retro undercount cross-repo usage (missed BioRemote's 19 plugin-namespaced calls).

**Scope: shipped `skills/**` + `ai_docs/prompts/roslyn-mcp-multisession-retro.md` only** — maintainer-local `.claude/**` and `ai_docs/**` dev docs legitimately use the bare dev-build prefix; do NOT touch. deps: none (VERIFY-FIRST gates severity).

## Amendment — 2026-08-10 (backlog-sweep 20260810T175048Z, PR #1204 — bounded batch shipped, row stays OPEN)

- **VERIFY-FIRST bullet is now SATISFIED — the misfire is CONFIRMED.** Acceptance bullet 1 asked whether the entry gate misfires for plugin consumers. It does: `SKILL.md` Step 2 routes every run into `prompts/quick.md` or `prompts/full.md`, and both re-imposed the identical bare-literal halt (`quick.md:5,41`, `full.md:9`, `phases/setup-and-analysis.md:12,14`). A plugin-install consumer cleared Step 1 and then halted ten lines later in the routed prompt. Per this row's own acceptance, that confirmation is the trigger to bump severity above Low.
- **SHIPPED in PR #1204 (4 production files, at the Rule 3 cap):** `skills/mcp-server-surface-test/SKILL.md`, `prompts/quick.md`, `prompts/full.md`, `prompts/phases/setup-and-analysis.md`. All gate sites now use **resolve-once-then-pin**: scan for every tool whose name ends in `server_info` (expect multiple candidates from other servers), identify the Roslyn one by response shape (`connection.state` + `catalogVersion` + `surface.*`), then pin THAT prefix and resolve every later bare name under it only. Halt only when no candidate returns a Roslyn-shaped response.
- **Why pinning, not plain suffix matching.** A first attempt used an open-ended suffix probe and a cold review traced it to a WORSE defect than the original: `find_references` and `get_symbol_outline` are also exposed by the `python-refactor` (Jedi) MCP server, so later bare names could resolve to another server and the audit would attribute Jedi results to Roslyn — in findings this skill auto-files upstream as GitHub Issues. Any future wording MUST preserve the pin.
- **REMAINING SCOPE (this row stays open):**
  - `skills/analyze/SKILL.md` — deliberately reverted to base in PR #1204 to hold the 4-file cap.
  - The remaining ~18 shipped `skills/**` files + `ai_docs/prompts/roslyn-mcp-multisession-retro.md`.
  - `skills/mcp-server-surface-test/README.md:14` — still a bare literal; confirmed OFF the executable path (nothing in SKILL.md/prompts reads it), so cosmetic, not a gate.
  - Acceptance bullet 3's guard: extend `eng/verify-skills-are-generic.ps1` to scan `skills/**/*.md` (it currently scans only `SKILL.md`, and its banned-pattern list has no `mcp__` entry) AND to assert the canonical note stays byte-identical wherever it appears — otherwise the ~18-file sweep drifts 22 unpinned copies.
  - Note polish deferred from #1204: restructure the ~340-word prose note into 3 bullets, trim the `SKILL.md` frontmatter `description` (now 944 chars, ~80 from the 1024 platform cap), de-duplicate the `full.md` vs `phases/setup-and-analysis.md` copy (both on one execution path), and fix "tool list returns a Roslyn-shaped response" (a tool list does not return responses) at `quick.md:5` / `full.md:9`.
- The canonical note is currently byte-identical in all 4 files (independently hashed). Preserve that when sweeping.
## Amendment — 2026-08-13 (backlog-sweep 20260813T172325Z; PRs #1233, #1232, #1240 — row stays OPEN)

Three split initiatives shipped against this row. **None closed it, and none should have** — acceptance bullets 3 and 4 remain unmet.

**Shipped:**
- **PR #1233** (6.01) — resolve-once-then-pin precheck added to `skills/analyze/SKILL.md`, `architecture-review`, `complexity`, `di-audit` (4 files, at the Rule 3 cap).
- **PR #1232** (6.02) — `ai_docs/prompts/roslyn-mcp-multisession-retro.md`, `skills/refactor/SKILL.md`, `skills/refactor-loop/SKILL.md`, `skills/mcp-server-surface-test/README.md` (the README bare literal noted here as cosmetic is now fixed).
- **PR #1240** (6.03) — genericity guard broadened from `skills/**/SKILL.md` to `skills/**/*.md`, placeholder-root stripping added, `schemaVersion` retired from the banned list (verified false positive: its sole occurrence is the skill's own emitted scorecard JSON).

**Still open — the two acceptance bullets that matter:**
- Bullet 3's **prefix-agnostic assertion** was deliberately NOT added by 6.03. Adding it before the remaining files are swept would red CI against them.
- Bullet 4's **regression** (no bare `mcp__roslyn__` literal inside a verify-it-appears instruction) is therefore also unshipped. `mcp__` is still not in `$bannedPatterns`.
- Roughly 10 of the ~18 shipped files remain unswept.
- **Byte-identity guard still unbuilt.** The precheck block is now byte-identical across the 6.01 files (hash verified during review) but nothing asserts it. As the remaining files are swept this becomes ~17 unpinned copies free to drift. Both the 6.01 and 6.02 reviews independently routed this here as an amendment rather than a sibling row, because a sibling would collide as a second PR editing the same guard.

**New finding folded in (PR #1232 review, traced not hypothesized):** the retro prompt's new relevance regex `mcp__\S*roslyn\S*__\S+` works as a boolean filter but is unusable for the extraction the same prompt mandates — Claude Code JSONL is minified (measured non-whitespace runs up to 58,809 chars), so `rg -o` returns multi-KB JSON spans instead of a tool name. It is also case-sensitive, so a `Roslyn` registration key is a silent false negative. Use an identifier-bounded, case-insensitive form, e.g. `(?i)mcp__[A-Za-z0-9_.-]*roslyn[A-Za-z0-9_.-]*__[a-z0-9_]+`. Confirmed NOT a relevance-filter defect: shipped vs bounded regex matched an identical 72 files across a 726 MB session corpus.
## Amendment — 2026-08-25 (backlog-sweep 20260825T151721Z; PRs #1342, #1341, #1353 — row stays OPEN)

Three split initiatives shipped. **The row was deliberately NOT closed**, correcting the plan's original intent.

**Shipped:**
- **PR #1342** (batch-a) — resolve-once-then-pin precheck applied to `exception-audit`, `format-sweep`, `generate-tests`, `impact-assessment`.
- **PR #1341** (batch-b) — same for `inheritance-explorer`, `modernize`, `nuget-preflight`, `review`.
- **PR #1353** (guard) — acceptance bullets 3 and 4: `eng/verify-skills-are-generic.ps1` now carries `Assert-PrefixAgnostic` (contextual imperative rule, not a blanket ban) and `Assert-CanonicalBlockIdentity` (byte-identity of the canonical block), backed by `eng/banned-skill-markers.json`'s `prefixAgnostic` + `canonicalPrecheckBlocks` and `tests/RoslynMcp.Tests/Skills/SkillPrefixGenericityTests.cs`.

**Why the row stays open.** The plan originally had the guard initiative close this row. That was corrected at plan time: acceptance bullet 4 is NOT met while five skills still carry the old bare-prefix block behind `residualUnsweptAllowlist`. Closing would have banked a five-file amnesty as a satisfied criterion.

**Verified remaining work (measured 2026-08-25, not estimated):** exactly 5 unswept files, matching the allowlist —
`skills/semantic-find/SKILL.md`, `skills/test-coverage/SKILL.md`, `skills/trace-flow/SKILL.md`, `skills/version-bump/SKILL.md`, `skills/workspace-health/SKILL.md`.
(`skills/mcp-server-surface-test/SKILL.md` still shows three `mcp__roslyn__` literals but is already on the pinned form — its literals are the "examples, not an allowed list" prose, and the guard passes it.)

**Next deliverable:** a batch-c sweep of those 5 files, after which `residualUnsweptAllowlist` empties and the row closes. `skills/workspace-health/SKILL.md` carries a longer per-skill variant of the block — the identity guard tolerates a trailer after the terminator, so keep the canonical leading slice byte-identical.

**Correction to the 2026-08-13 amendment:** it said "roughly 10 of the ~18 shipped files remain unswept". The live count at sweep start was 13, not ~10.

**Related new row:** `skill-prefix-guard-tests-assert-bcl-not-gate` — the guard's own negative-path tests assert BCL string semantics rather than exercising the gate.
