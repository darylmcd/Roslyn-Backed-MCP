# 04 — C3 Prompts & LLM-facing copy

Rows (check C3):

- `prompt-argument-binding-spec-strings-and-names` (High) — prompts/get rejects spec string args for int params and never names the bad argument
- `discover-capabilities-unknown-category-matches-all` (Low) — discover_capabilities returns the full surface for an unknown category
- `analyze-dependencies-prompt-unranked-node-cap` (Low) — analyze_dependencies prompt keeps 50 low-value external nodes
- `get-prompt-text-description-cites-list-prompts` (Low) — get_prompt_text description cites a nonexistent list_prompts

Evidence: `raw/phase-G8.md`, `raw/g8-prompt-*.txt` (20 renders), `raw/head-prompt-negative.json`. 20/20 prompts render with no hallucinated tool names and idempotent output.
