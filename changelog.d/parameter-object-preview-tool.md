---
category: Added
---

- **Added:** Experimental `parameter_object_preview` MCP tool. Groups N parameters of a method into a positional sealed-record DTO and rewrites every call site atomically to wrap the grouped arguments in `new Dto(...)`. Refuses default-value sites, `ref`/`out`/`in`/`params`, the `this` parameter on extension methods, and local-function targets; warns on reflective callers. Cross-project rewrites only when every caller-project already references the DTO project (no auto-`<ProjectReference>` insertion). Closes `parameter-object-preview-tool`.
