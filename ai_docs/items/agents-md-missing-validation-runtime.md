# agents-md-missing-validation-runtime — `AGENTS.md` lacks the doc-audit v24 required `## Validation runtime` section

**row:** `agents-md-missing-validation-runtime` · **pri:** `Medium` · **size:** `S`

## Anchors

- `AGENTS.md:71`
- `ai_docs/runtime.md:28`

## Acceptance

- [ ] `AGENTS.md` gains the doc-audit STANDARD v24 required `## Validation runtime` section, placed after `## Breaking-change posture`, with the canonical 9-column header.
- [ ] Every column carries measured data (command, measured duration + measurement date, Bash timeout or background, hooks and their runtime, CI-equivalent test filter, regen companions, flake registry, `parallelSafe`) — not the template placeholder.
- [ ] `ai_docs/runtime.md` and `ai_docs/prompts/backlog-sweep-addenda.md` point at it instead of duplicating the same facts.

## Evidence

`AGENTS.md` sections are: File Purpose, Standing Engineering Directives, Canonical Rule Sources, Session Start, Conflict Precedence, Default Behavior, Breaking-change posture, Planning Scope. There is no `## Validation runtime`, which doc-audit STANDARD v24 makes a required section (finding `agents-md-missing-validation-runtime`, hard / approval-required).

The facts it should carry already exist but are scattered and partly stale: `ci_equivalent` and `parallelSafe` in `ai_docs/prompts/backlog-sweep-addenda.md`, hook runtimes in `.claude/settings.json`, the CI-equivalent recipe in the `justfile` (`just ci`), and the flake registry in `ai_docs/known-flakes.md`. This audit found the addenda's `ci_equivalent` had drifted from both `.github/workflows/ci.yml` and `just ci` — the exact drift a single canonical section prevents.
