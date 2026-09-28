---
category: Fixed
---
- **Fixed:** `workspace_close` no longer advertises its internal `getProcessesByName` / `processDrainTimeout` test seams in its input schema, so clients can no longer see or set the process-drain timeout. A new schema-hygiene test fails when any tool exposes an undocumented or unsupported-type parameter. (`workspace-close-schema-leaks-test-seams`)
