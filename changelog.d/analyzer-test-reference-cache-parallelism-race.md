---
category: Fixed
---

- **Fixed:** Isolated every release-gate testhost behind a unique temporary root and serialized the three Microsoft.CodeAnalysis.Testing analyzer-harness classes, preventing its `test-packages/Microsoft.NETCore.App.Ref.8.0.0` reference cache from remaining partially extracted across self-hosted CI jobs or concurrent analyzer tests (`analyzer-test-reference-cache-parallelism-race`).
