---
name: surface-audit
description: One-pass audit of the Roslyn MCP server's live surface (tools / resources / prompts / shipped skills) against documentation count claims. Use when preparing a release, chasing doc drift, or answering "how many tools does this server have?" without burning a dozen greps. Calls server_info, globs skills/*/SKILL.md, greps README / CHANGELOG / ai_docs / docs for numeric surface claims (e.g. "X tools", "Y prompts", "Z skills"), and reports drift as a compact table. Read-only; never edits docs.
---

# surface-audit

Cross-check the live server surface against every hard-coded count in the repo's documentation.

## Motivation

The 2026-04-10→04-24 retro showed multiple self-audit sessions spending significant grep budget verifying doc claims like "10 skills", "66 stable + 60 experimental = 126 tools", "123 tools". This skill does that walk in one pass so agents get an authoritative drift report without polluting the transcript.

## Steps

### 1. Collect live surface

Call `mcp__roslyn__server_info`. Record from the response:
- `surface.tools.stable`, `surface.tools.experimental`, `surface.registered.tools`
- `surface.resources.stable`, `surface.resources.experimental`, `surface.registered.resources`
- `surface.prompts.stable`, `surface.prompts.experimental`, `surface.registered.prompts`
- `version`, `catalogVersion`, `surface.registered.parityOk`

### 2. Count shipped skills on disk

`Glob skills/*/SKILL.md` — the count is the live skill count. Record as `shippedSkills`.

### 3. Count maintainer-only skills

`Glob .claude/skills/*/SKILL.md` — these are the repo-local override skills (not shipped). Record as `maintainerSkills`. Report the totals but DO NOT compare against shipped-skill count claims (they're separate surfaces).

### 4. Sweep docs for numeric surface claims

Grep for patterns that match surface count claims. Regex list (case-insensitive):

```
[0-9]+\s*(stable|experimental)?\s*(tools?|resources?|prompts?|skills?)
```

Scope the grep to:
- `README.md`
- `docs/**/*.md`
- `ai_docs/**/*.md` (skip `ai_docs/archive/**` and `ai_docs/reports/**` — historical snapshots, drift expected)
- `CHANGELOG.md` — ONLY the `## [Unreleased]` section; shipped version sections are frozen history
- `.claude-plugin/plugin.json` + `.claude-plugin/marketplace.json` — description strings

For each hit, capture: file, line, the exact claim text, the claimed count.

### 5. Compute drift

Produce a table:

| Claim source | Claim text (excerpt) | Claimed | Live | Drift |
|---|---|---|---|---|
| `docs/setup.md:42` | "107 stable tools and 54 experimental" | 107 / 54 | 107 / 54 | ok |
| `README.md:18` | "over 150 Roslyn-powered tools" | 150 | 161 | ok (+11 live, claim is conservative) |
| `ai_docs/references/tool-usage.md:9` | "...10 shipped skills..." | 10 | 12 | **drift +2** |

Drift categories:
- `ok` — claim exactly matches or is a conservative lower bound (e.g. "over N" where live > N)
- `drift +N` / `drift -N` — claim is stale; report the delta
- `unverifiable` — claim is about a category we can't measure from `server_info` or globs (e.g. "dozens of diagnostics"). Flag but don't count as drift.

### 6. Report

Output order:

1. **Live surface summary** — 4-line block: version, tools, resources, prompts, skills (shipped / maintainer).
2. **Drift table** — only rows where drift ≠ ok. If clean, say "no drift found across N scanned files."
3. **Unverifiable claims** — separate table, for human review.
4. **Suggested next steps** — a 1-3 line call-out naming the files to edit (but DO NOT edit them; this skill is read-only).

## Non-goals

- Does NOT edit documentation. The user decides which claims to update and in what PR.
- Does NOT re-audit the old `## [1.x.y]` CHANGELOG sections. Frozen history is expected to drift from live.
- Does NOT count backlog rows, plan initiatives, or CI steps — those are tracked elsewhere.
