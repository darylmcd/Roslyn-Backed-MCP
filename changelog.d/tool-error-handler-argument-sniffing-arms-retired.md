---
category: Changed — BREAKING
---
- **Changed — BREAKING:** The error handler no longer recognizes plain `ArgumentException` values by parameter name or message text (`projectName`, `parametersJson`, `pattern` regex sentinel, "Unsupported catalog diff", incomplete `filePath`/`column`); every shipped producer already throws a public-message carrier with its own guidance, so released tool messages are unchanged. A plain `ArgumentException` now always reports the generic "Parameter '<name>' is invalid..." message, and the unused `SemanticGrepService.InvalidRegexSentinel` constant is removed. Migration: none for MCP callers; embedders that relied on plain-exception guidance must throw `PublicArgumentException`.
