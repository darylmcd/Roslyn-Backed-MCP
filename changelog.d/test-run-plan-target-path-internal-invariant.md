---
category: Maintenance
---
- **Maintenance:** `TestRunnerService.RunTestCommandAsync` reports a plan without a target path as an internal `InvalidOperationException` (server-only detail) instead of an `ArgumentException`; the plan is server-built, so this is an invariant, not a caller error. Prepares the production ban on `ArgumentException` constructors (`argument-exception-public-message-guard`).
