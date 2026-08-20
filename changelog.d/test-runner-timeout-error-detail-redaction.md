---
category: Fixed
---

- **Fixed:** `test_run` no longer echoes raw timeout-exception text in its failure envelope. The `Timeout` envelope's `summary` is now a deterministic, secret-safe projection carrying the timeout category, non-retryability, command shape, configured budget, recovery guidance, and a correlation id, and `stdErrTail` is no longer populated from the exception message — the absolute project/results paths and the caller-supplied `--filter` value are no longer published. Full exception topology is routed to the opt-in server diagnostic sink under the new `TestRun` category. Caller cancellation remains distinct and continues to propagate.
