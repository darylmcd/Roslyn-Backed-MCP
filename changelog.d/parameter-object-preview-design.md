---
category: Added
---

- **Added:** Design note `ai_docs/items/parameter-object-preview-design.md` for a future `parameter_object_preview` MCP tool. Specifies the tool contract, generated-DTO conventions (positional `record`), call-site rewrite policy (refuse default-value sites and `ref`/`out`/`in`/`params`/`this`/local-function targets; warn on reflective sites), cross-project pre-flight check, and Rule-3 file-count estimate (4 structural units / 6 prod files + 3 addenda + 1 test). Closes `parameter-object-preview-design`; opens follow-on row `parameter-object-preview-tool`.
