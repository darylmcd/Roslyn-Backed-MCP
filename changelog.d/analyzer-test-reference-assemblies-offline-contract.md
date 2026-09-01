---
category: Fixed
---

- **Fixed:** Analyzer tests now restore their exact .NET 8 reference pack up front, so test execution no longer performs a late NuGet download after sources become unavailable; timing-sensitive sampling MRTR wire tests also run outside the suite's 24-worker parallel pool. Closes `analyzer-test-reference-assemblies-offline-contract` and `sampling-mrtr-wire-suite-contention-isolation`.
