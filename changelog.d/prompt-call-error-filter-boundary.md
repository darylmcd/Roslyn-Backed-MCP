---
category: Fixed
---

- **Fixed:** Prompt failures now travel the JSON-RPC error channel through a single `get_prompt` boundary filter (`GetPromptErrorFilter`) instead of being returned as successful user-role prompt messages. Unexpected prompt exceptions are projected to a sanitized `InternalError` (`-32603`) carrying only a category, remediation, and correlation id — the raw exception message, type chain, inner exceptions, stack text, and local paths are no longer disclosed to clients; server-side diagnostics retain the full secret-safe structure under the new `GetPrompt` observability category. The legacy `PromptMessageBuilder.CreateErrorMessage` body is sanitized the same way until the per-handler catches are retired. Cancellation and parameter-validation (`InvalidParams`) semantics are unchanged. Compatibility note: clients that previously received a successful `prompts/get` result describing a failure now receive a JSON-RPC error response instead.
