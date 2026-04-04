# MCP deep-review audit reports

Use the living prompt [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) with an MCP client connected to **roslyn-mcp** to exercise the full tool surface against a real solution.

## Outputs

- **Single audit file** per run (PASS/FLAG/FAIL per tool, performance notes, schema-vs-behavior findings).
- **FAIL** items that represent product defects should be tracked in **`ai_docs/backlog.md`** (open rows only).

## Baseline session template

| File | Purpose |
|------|---------|
| [`deep-review-baseline-2026-04-04.md`](deep-review-baseline-2026-04-04.md) | Structure and checklist for a full audit; update when the tool/resource/prompt surface changes. |

Re-run the deep-review prompt after major surface changes (see appendix version line in the prompt).
