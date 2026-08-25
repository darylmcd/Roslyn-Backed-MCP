---
category: Changed
---

- **Changed:** Trimmed method-level tool descriptions for the symbol and analysis tool surface (`symbol_search`, `find_references`, `find_overrides`, `get_completions`, `semantic_grep`, `semantic_search`, `find_duplicated_methods`, `project_diagnostics`, and 32 more) to ~200-char capability statements, removing prose that duplicated each tool's published `outputSchema` and its own parameter descriptions. Cuts ~14.7k characters (~3.7k tokens) from every `tools/list` response; each tool keeps its discriminating trigger for tool-search discovery.
