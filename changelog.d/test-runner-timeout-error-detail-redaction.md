---
category: Fixed
---

- **Fixed:** `test_run` no longer echoes raw timeout-exception text in its failure envelope. The `Timeout` envelope's `summary` is now a deterministic, secret-safe projection carrying the timeout category, non-retryability, command shape, configured budget, recovery guidance, and a correlation id, and `stdErrTail` is no longer populated from the exception message, so neither carries raw exception text, absolute paths, or the caller-supplied `--filter` value. (The structured `execution` block of the same response still reports the command shape it always did; narrowing that is tracked separately.) Full exception topology is routed to the opt-in server diagnostic sink under the new `TestRun` category. Caller cancellation remains distinct and continues to propagate.
