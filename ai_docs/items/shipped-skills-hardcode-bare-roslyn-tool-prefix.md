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
