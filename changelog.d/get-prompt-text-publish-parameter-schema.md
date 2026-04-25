---
category: Added
---

- **Added:** Prompt catalog resource (`roslyn://server/catalog/prompts/*`) now publishes `parameters[]` per prompt — each entry carries `name`, `type`, `required`, `defaultValue`, and `description` sourced from `[McpServerPrompt]` attributes at startup (`ServerSurfaceCatalog` + new `PromptParameterIndex`). Closes `get-prompt-text-publish-parameter-schema`.
