---
category: Fixed
---

- **Fixed:** Direct solution test runs now restore every owned sample fixture through the same fail-closed preparation script used by `just` and release validation, including fresh checkouts with no sample `obj` state. Closes `standalone-test-fixture-restore-contract`.
