---
category: Added
---

- **Added:** `SymbolDto`, `DiagnosticDto`, `TypeUsageDto`, `PropertyWriteDto`, and `MutationCallerDto` now expose an optional camel-case `location` object alongside their existing flat location fields. Producers populate both representations from one resolved span; partial diagnostics keep `location: null` instead of fabricating coordinates. Existing flat fields and positional constructor calls remain supported. Consumers using strict unknown-property validation must allow `location`, and equality-based consumers should include the nested value when comparing producer results.
