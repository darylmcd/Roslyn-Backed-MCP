| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `AGENTS.md` lacks the required canonical runtime table while `runtime.md`, the addenda, hook configuration, and CI policy duplicate or scatter the facts. A single measured section in the bootstrap document prevents the already-observed CI-equivalent drift. |
| Approach | - [ ] `AGENTS.md` gains the doc-audit STANDARD v24 required `## Validation runtime` section, placed after `## Breaking-change posture`, with the canonical 9-column header.<br>- [ ] Every column carries measured data (command, measured duration + measurement date, Bash timeout or background, hooks and their runtime, CI-equivalent test filter, regen companions, flake registry, `parallelSafe`) — not the template placeholder.<br>- [ ] `ai_docs/runtime.md` and `ai_docs/prompts/backlog-sweep-addenda.md` point at it instead of duplicating the same facts. |
| Scope | Documentation files: `AGENTS.md`, `ai_docs/runtime.md`, `ai_docs/prompts/backlog-sweep-addenda.md`, and `changelog.d/agents-md-missing-validation-runtime.md`. Read `.claude/settings.json`, `justfile`, `CI_POLICY.md`, and `ai_docs/known-flakes.md` as evidence only. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | The table must distinguish the authoritative `just ci` local mirror from hosted shard topology without restating every CI detail. Retain the addenda's machine-readable `ci_equivalent` and `parallel_safety` keys because global remediation tooling parses them; replace only duplicative explanatory prose with pointers to AGENTS.md. Measure the gate in this execution and date the observation; keep hook timing and `parallelSafe` claims traceable to live configuration. This is not refactor-shaped, so fanout is not applicable. |
| Validation | Time one foreground `just ci` invocation used for this initiative's final gate and record its wall time/date; verify the exact 9-column header; ensure runtime.md and the addenda's explanatory prose link to the new section while the addenda still exposes parseable `ci_equivalent` and `parallel_safety` keys; run `pwsh -NoProfile -File ./eng/verify-ai-docs.ps1`, `pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1`, and `just ci`. |
| Performance review | N/A — documentation consolidation. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add a measured canonical validation-runtime contract and replace duplicated runtime facts with pointers. |
| Backlog sync | Close rows: [agents-md-missing-validation-runtime]. |
