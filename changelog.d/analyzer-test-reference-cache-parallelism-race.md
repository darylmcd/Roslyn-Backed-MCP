---
category: Fixed
---

- **Fixed:** Serialized the three Microsoft.CodeAnalysis.Testing analyzer-harness classes that share its process-global reference-assembly extraction cache, preventing parallel first-use from leaving `test-packages/Microsoft.NETCore.App.Ref.8.0.0` partially missing and cascading into analyzer-test failures (`analyzer-test-reference-cache-parallelism-race`).
