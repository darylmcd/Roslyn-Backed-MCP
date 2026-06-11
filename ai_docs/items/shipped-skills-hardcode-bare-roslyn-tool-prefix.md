# shipped-skills-hardcode-bare-roslyn-tool-prefix — make consumer-facing artifacts prefix-agnostic

**row:** `shipped-skills-hardcode-bare-roslyn-tool-prefix` · **pri:** `Low` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

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
