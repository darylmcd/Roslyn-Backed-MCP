---
category: Fixed
---

- **Fixed:** Closed the top remediation batch by returning structured errors for zero-project `compile_check` filters, excluding single-delegation MCP tool wrappers from duplicate-method clusters by default, listing all missing `get_prompt_text` parameters in one response, mapping metadata-only `goto_type_definition` failures to `NotFound`, and recording the formatter check-mode baseline policy. Closes `compile-check-project-filter-miss-no-error-envelope`, `find-duplicated-methods-mcp-wrapper-false-positive`, `get-prompt-text-multi-step-required-param-errors`, `goto-type-definition-builtins-invalidoperation`, and `formatter-check-mode-baseline-policy`.
