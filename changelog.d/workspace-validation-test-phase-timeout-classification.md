---
category: Fixed
---

- **Fixed:** Experimental validation bundles preserve related-test phase deadlines as retryable `timeout` results instead of non-retryable `test-failure` results. Migration: handle the `Timeout` failure envelope and retry the test phase; genuine runner failures and caller cancellation retain their existing behavior. See [ADR 0010](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/docs/decisions/0010-validation-verdict-completeness.md). Closes `workspace-validation-test-phase-timeout-classification`.
