| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | The live retro prompt still treats file mtime as the selection rule and current Codex calls as namespace-bearing records, so following it literally can omit in-window sessions and find zero Roslyn calls. The correction is isolated to the prompt contract. |
| Approach | - [ ] §0 **Window** no longer selects on file mtime; the stated rule is record-timestamp containment within both window bounds. Codex takes no mtime prefilter; any Claude-side mtime use is explicitly labeled a widened pre-filter, not the selection criterion.<br>- [ ] §0 sources table and the tool-name-matching paragraph describe the current Codex shape: `custom_tool_call` named `exec`, JS input awaiting a `tools.<flat tool name>(...)` call, result in `custom_tool_call_output` matched by `call_id`.<br>- [ ] The Codex match rule is prefix-agnostic — a pattern equivalent to `tools\.mcp__\S*roslyn\S*__\S+` — with `mcp__roslyn__` named only as the form observed in the current window, never as the required literal.<br>- [ ] The `payload.namespace`-contains-`roslyn` instruction is removed or demoted to a legacy-record fallback, so a current-window run cannot come up empty by following the spec literally.<br>- [ ] Prefix-agnostic matching on the Claude side and flat-name normalization for the report are preserved (do not regress the fix shipped at CHANGELOG.md line 263).<br>- [ ] The `truncated: true` escape hatch and per-source count reporting in §0 are unchanged. |
| Scope | Documentation files: `ai_docs/prompts/roslyn-mcp-multisession-retro.md` and `changelog.d/retro-prompt-window-and-codex-extraction-rules.md`. No server, registration, or test changes. |
| Tool policy | edit-only |
| Estimated context cost | 12000 |
| Risks | Preserve the two sources' distinct prefilter behavior and keep the regex client-prefix agnostic. Avoid turning an observed flat name into a required literal. This is not refactor-shaped, so fanout is not applicable. |
| Validation | Re-read §0 and grep for the retired mtime-selection and namespace-only instructions; run `pwsh -NoProfile -File ./eng/verify-ai-docs.ps1`, `pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1`, and `just ci`. |
| Performance review | N/A — prompt documentation only. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Correct the multi-session retrospective window and Codex tool-call extraction rules for current transcript records. |
| Backlog sync | Close rows: [retro-prompt-window-and-codex-extraction-rules]. |
