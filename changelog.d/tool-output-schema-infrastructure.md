---
category: Added
---

- **Added:** MCP 2025-06-18 `outputSchema` + `structuredContent` infrastructure — `[McpToolMetadata]` accepts an optional `outputSchemaTypeRef`, schemas generate from existing DTO records via `System.Text.Json` `JsonSchemaExporter`, `StructuredCallToolFilter` emits both `content[].text` and `structuredContent` channels with a single `_meta` payload (no duplication). New `ToolOutputSchemaIndex` reflection cache mirrors the `PromptParameterIndex` pattern. No tools opted in this PR — per-tool batches follow. Closes `tool-output-schema-infrastructure`.
