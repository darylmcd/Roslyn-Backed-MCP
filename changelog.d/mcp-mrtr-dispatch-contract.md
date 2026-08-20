---
category: Fixed
---

- **Fixed:** Server-driven input now works on stateless MCP sessions. `InputRequiredException` is rethrown from the `tools/call` filter instead of being converted into an `isError` tool result, and a new request-scoped input adapter (`RequestScopedInputAdapter`) emits `InputRequiredResult` on MRTR-capable clients while consuming only that request's `inputResponses` on retry. Clients negotiating `2025-11-25` keep the existing direct `elicitation/create` behavior.
