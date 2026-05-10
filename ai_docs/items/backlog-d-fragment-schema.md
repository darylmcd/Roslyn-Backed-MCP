# `backlog.d/` fragment schema

<!-- purpose: Define the on-disk shape of `backlog.d/<finding-id>.md` fragments produced by /mcp-server-stress and consumed by /backlog-intake. -->
<!-- scope: in-repo, cross-repo (siblings) -->

This document is the canonical schema for **`backlog.d/` fragments** — the cross-repo audit-finding artifact written by `/mcp-server-stress` runs in audited repos and consumed by `/backlog-intake` in this repo.

## Why fragments

The original cross-repo flow had `/mcp-server-stress` (running in audited repo X) write `*_mcp-server-audit.md` reports either directly into `<Roslyn-MCP-root>/ai_docs/audit-reports/` (cross-write) or into a local `audit-reports/` and rely on the operator to copy them later. Both paths are fragile:

- Cross-writes race when several repos audit concurrently.
- Operator-driven copies are routinely forgotten, leaving findings stranded in the audited repo with no consolidation.
- The whole prose `.md` report is heavyweight evidence; intake only needs the *actionable* per-finding extracts plus a back-pointer to the report.

The `changelog.d/` pattern at `.claude/skills/draft-changelog-entry/` + `.claude/skills/bump/` already proves the fragment-then-consolidate flow for in-repo release notes. This schema applies the same idea to cross-repo audit findings:

- **Audit run emits one fragment per actionable finding** into the audited repo's local `backlog.d/`.
- **`/backlog-intake` walks configured sibling repos**, dedupes fragments against existing rows in `<Roslyn-MCP-root>/ai_docs/backlog.md`, appends new rows, and **deletes consumed fragments at the source**. The deletion *is* the consumption record — no manifest is kept.
- **Re-running intake on a clean `backlog.d/` is a no-op.**

The prose `*_mcp-server-audit.md` and the scorecard JSON stay where the audit run wrote them (under `<X>/ai_docs/audit-reports/`); fragments are the only artifact intake consumes for new-row creation. The prose report remains available for cross-reference via the fragment's `source_audit` field.

## On-disk path

Each fragment lives at:

```
<audited-repo-root>/backlog.d/<finding-id>.md
```

- `<audited-repo-root>` is the root of the audited repo (the repo `/mcp-server-stress` ran against), NOT this Roslyn-Backed-MCP repo.
- `<finding-id>` is a stable, slug-form identifier (kebab-case) that uniquely names the finding *within the audited repo*. It is the same value as the frontmatter `id` field — the filename and the `id` MUST match.

Example layout for an audit run on a sibling repo `tradewise`:

```
C:/Code-Repo/TradeWise/
├── ai_docs/
│   └── audit-reports/
│       └── 20260507T203015Z_tradewise_mcp-server-audit.md   # prose report (stays here)
└── backlog.d/
    ├── tradewise-find-references-stale-cache.md             # fragment 1
    └── tradewise-symbol-search-empty-query-overflow.md      # fragment 2
```

## Frontmatter (required)

Each fragment is a markdown file with YAML frontmatter, then a single-paragraph body. All frontmatter fields are required.

| Field | Type | Notes |
|---|---|---|
| `id` | string (kebab-case) | Stable hash/slug of the finding. MUST match the filename (sans `.md`). Used for dedup. Should encode the audited repo's id where finding could collide with similar findings in other repos (e.g. `roslyn-mcp-find-references-stale-cache`, `tradewise-find-references-stale-cache`). |
| `source_audit` | string (relative path) | Filename of the source `*_mcp-server-audit.md` report **within the audited repo's `ai_docs/audit-reports/`**, e.g. `20260507T203015Z_tradewise_mcp-server-audit.md`. Intake uses this to back-reference evidence when the fragment alone is ambiguous. |
| `source_repo` | string (kebab-case) | Repo id, e.g. `roslyn-backed-mcp`, `tradewise`, `it-chat-bot`. Disambiguates `id` collisions when two repos independently produce the same slug. The `(source_repo, id)` pair is the dedup key. |
| `severity` | enum | One of `P0` (critical, ship-blocker), `P1` (high), `P2` (medium), `P3` (low). Maps to the priority bands in `ai_docs/backlog.md` so intake can place the row without re-classifying. |
| `area` | enum | One of `tools`, `resources`, `prompts`, `skills`, `concurrency`, `perf`, `docs`, `security`. Coarse classification used by intake for grouping and by the planner prompt for context-budget sizing. **Note:** `area: security` is a load-bearing pre-disclosure refusal token — both `/mcp-server-surface-test --auto-file` and `/backlog-intake --publish` refuse to file such fragments to public GitHub Issues, regardless of severity. |
| `server_version` | string (semver) | Roslyn MCP server version captured during the audit run (`server_info.version` at Phase -1). Required as of Row 2 of the move-to-git-issues design. Lets the planner correlate findings to a specific server version when triaging a regression. |
| `anchors` | list of `file:line` strings | One or more `path/to/file.cs:NNN` entries pointing at the live code or doc the finding cites. Relative to the audited repo's root. May be a single-element list. |

## Body

After the frontmatter, the body is a **single paragraph** combining three things in order:

1. **Finding** — one to two sentences describing the bug or gap.
2. **Repro** — one or two sentences describing the minimal reproduction (which tool / inputs / expected vs. actual).
3. **Proposed fix sketch** — one or two sentences pointing at the likely fix shape (which file / which method / what change). Intake does not promote this verbatim into the backlog; the planner prompt re-expresses it as a `do` cell during sweep planning.

Keep the body tight (≤6 sentences total). Long-form analysis belongs in the source audit report; the fragment is a pointer + actionable summary.

## Lifecycle

```
audit run in repo X            /backlog-intake (this repo)
─────────────────────          ────────────────────────────
emit <X>/backlog.d/<id>.md ──> walk siblings + this repo
                               collect fragments
                               dedupe by (source_repo, id)
                               append new rows to ai_docs/backlog.md
                               delete consumed fragments at <X>/backlog.d/<id>.md
```

After intake runs, the audited repo's `backlog.d/` contains only fragments that *could not* be consolidated (e.g. dedup match against an existing row, or intake detected they had been superseded). Those are left in place for the next intake pass; intake is idempotent on already-consolidated input.

## Dedup rule

Intake dedupes fragments before appending rows in this order:

1. **Exact `(source_repo, id)` match against existing rows.** If `ai_docs/backlog.md` already has a row whose id equals the fragment's `id` AND its evidence cites the same `source_repo`, the fragment is a duplicate. Drop it; delete the source fragment.
2. **Anchor-set similarity.** If two fragments from different repos cite a substantial overlap of anchors (≥50 % of paths overlap, intersection on the same shared code), intake folds them into a single row whose `do` cell mentions both source repos. Common case: a Roslyn-MCP server bug observed independently from two different audited repos — same code, two reporters.
3. **Anchors map to already-shipped CHANGELOG entries.** Same Phase 2 verify-not-already-shipped logic the intake skill already runs.

Anything not deduped becomes a new row; the source fragment is deleted only after the row is written.

## Disambiguating fragment `.md` from audit-report `.md`

`eng/stage-review-inbox.ps1` discovers both prose audit reports (`*_mcp-server-audit.md`) and `backlog.d/*.md` fragments. They are disambiguated by **frontmatter shape**:

- **Fragment `.md`** has `id:` / `source_audit:` / `severity:` keys in YAML frontmatter.
- **Audit-report `.md`** has `generated_at:` / `window:` / `mode:` keys (or no frontmatter at all — pre-frontmatter reports use a `## 1. Header` H2 immediately).

The required disambiguator key is `severity:` — fragments always have it; audit reports never do. The discovery script pins on this key.

## Example fragment

`tradewise/backlog.d/tradewise-find-references-stale-cache.md`:

```markdown
---
id: tradewise-find-references-stale-cache
source_audit: 20260507T203015Z_tradewise_mcp-server-surface-test.md
source_repo: tradewise
severity: P2
area: tools
server_version: 1.35.0
anchors:
  - src/RoslynMcp.Roslyn/Services/FindReferencesService.cs:142
  - src/RoslynMcp.Host.Stdio/Tools/FindReferencesTool.cs:38
---

`find_references` returns stale results after a `rename_apply` against the same symbol within ~200ms; the bulk-cache window is not invalidated on rename. Repro: load `TradeWise.slnx`, call `rename_apply` on `OrderService.PlaceOrder`, immediately call `find_references` against the renamed symbol — the response cites the old name's locations. Proposed fix: invalidate `FindReferencesService`'s LRU bucket from `RenameService.Apply` before returning, mirroring the pattern in `MoveTypeService.Apply`.
```

After `/backlog-intake` consumes this fragment, a new row appears in `ai_docs/backlog.md` and the file at `tradewise/backlog.d/tradewise-find-references-stale-cache.md` is deleted.

## Validation rules (intake side)

Intake refuses a fragment (does not delete it; logs a warning) when any of:

- Missing required frontmatter key (including `server_version` as of Row 2).
- `severity` not in the documented enum.
- `area` not in the documented enum (note: `security` is now a member of the enum and triggers the public-filing refusal, not validation rejection).
- `anchors` empty.
- Filename does not match `id`.
- `source_repo` empty or contains characters outside `[a-z0-9-]`.
- `server_version` empty.

A malformed fragment is left in the source `backlog.d/` for the audit operator to fix and re-stage.
