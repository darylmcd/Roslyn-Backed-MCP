# retro-prompt-window-and-codex-extraction-rules — retro-prompt-window-and-codex-extraction-rules

**row:** `retro-prompt-window-and-codex-extraction-rules` · **pri:** `Medium` · **size:** `S`

## Context

`ai_docs/prompts/roslyn-mcp-multisession-retro.md` §0 carries two rules that no longer hold against the live transcript corpora, and both cost coverage in the 2026-09-13 run.

**(a) Window rule selects by file mtime.** Line 12 reads: "default last 14 days by file mtime". mtime is not a superset of the record-timestamp window at the start bound, and the two sources fail differently — so one blanket remedy is wrong. Codex: rollout mtime stays near file creation (466 of 504 in-window rollouts have records more than 60 s past mtime, by up to 98 h), so no finite widening of an mtime prefilter is safe; the prefilter hid 2 qualifying Codex parents that began minutes before the start bound and ran into the window, and the run set `truncated: true` as a result. Claude: session files on this machine were bulk-rewritten without adding timestamped records, which pulled 14 pre-window sessions in, but 0 of 439 pre-window Claude files contain a later record, so a widened mtime prefilter is still lossless there. Fix per the report's own rerun instruction: record-timestamp containment on both bounds is the selection rule; no mtime prefilter for Codex at all; any Claude-side mtime prefilter is a cheap, widened pre-filter only.

**(b) Codex extraction rule matches a namespace that no longer appears.** Line 21 states `.payload.namespace` carries the server (`mcp__roslyn`), and line 28 instructs "Codex: match a `namespace` containing `roslyn`". Codex now reaches the server only through the node-REPL exec tool: a `custom_tool_call` named `exec` whose JS input awaits `tools.mcp__<prefix>roslyn<suffix>__<tool>({...})`, with the result in a `custom_tool_call_output` keyed by `call_id`. No `function_call` record in the 14-day window carries a roslyn namespace — included rollouts show only `collaboration` and `clock` namespaces. The rule as written finds nothing. The §0 tool-call/tool-result table rows for Codex need the same correction. The replacement must stay prefix-agnostic: the Codex-side prefix is client-assigned exactly as the Claude-side one is, so the new rule matches a pattern, not the `mcp__roslyn__` literal observed today.

Prompt-file edit only. No server code, tool registration, or test changes.

## Anchors

- `ai_docs/prompts/roslyn-mcp-multisession-retro.md`

## Acceptance

- [ ] §0 **Window** no longer selects on file mtime; the stated rule is record-timestamp containment within both window bounds. Codex takes no mtime prefilter; any Claude-side mtime use is explicitly labeled a widened pre-filter, not the selection criterion.
- [ ] §0 sources table and the tool-name-matching paragraph describe the current Codex shape: `custom_tool_call` named `exec`, JS input awaiting a `tools.<flat tool name>(...)` call, result in `custom_tool_call_output` matched by `call_id`.
- [ ] The Codex match rule is prefix-agnostic — a pattern equivalent to `tools\.mcp__\S*roslyn\S*__\S+` — with `mcp__roslyn__` named only as the form observed in the current window, never as the required literal.
- [ ] The `payload.namespace`-contains-`roslyn` instruction is removed or demoted to a legacy-record fallback, so a current-window run cannot come up empty by following the spec literally.
- [ ] Prefix-agnostic matching on the Claude side and flat-name normalization for the report are preserved (do not regress the fix shipped at CHANGELOG.md line 263).
- [ ] The `truncated: true` escape hatch and per-source count reporting in §0 are unchanged.

## Evidence

The live prompt still carries both defects (verified 2026-09-13: line 12 "default last 14 days by file mtime"; line 21 `.payload.namespace`; line 28 "Codex: match a `namespace` containing `roslyn`"), and the 2026-09-13 retro run declared a spec exception plus `truncated: true` because of them. Independently corroborated against the live corpus: the newest archived rollout contains 115 `custom_tool_call` records, zero `namespace` fields, and recent rollouts carry 1,618 `tools.mcp__roslyn__compile_check` call sites inside exec JS.

- Report: `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — §0 "Spec exception (window rule)" (line 40, frontmatter `truncated: true`), Window-rule bullets (lines 101-103, 108), and truncation note 1 (line 175) "Codex has no namespace records for Roslyn."
- Finding id: `retro-prompt-window-and-codex-extraction-rules`
