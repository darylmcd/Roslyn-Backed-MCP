---
category: Fixed
---

- **Fixed:** `ElicitationChoicePrompt.TryElicitChoiceAsync`'s bare `catch { return null; }` around `server.ElicitAsync` absorbed `OperationCanceledException` alongside the two expected SDK failure shapes, so a cancelled elicitation request was indistinguishable from a deliberate user decline — callers (`SymbolTools` disambiguation paths) answered with the additive candidate-list response instead of surfacing the cancellation. The catch now names `InvalidOperationException` and `McpException` explicitly, logs at Debug, and lets `OperationCanceledException` (and any other genuinely unexpected exception) propagate. (elicitation-trychoice-cancellation-swallow)
