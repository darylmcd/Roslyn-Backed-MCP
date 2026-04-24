---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `test_related` now returns the same `{tests, dotnetTestFilter, pagination}` envelope as `test_related_files` instead of a bare `TestCaseDto[]` array. Callers that destructured the prior array must read `.tests` instead (`TestDiscoveryService`, `ITestDiscoveryService`). (`test-related-response-envelope-parity`)
