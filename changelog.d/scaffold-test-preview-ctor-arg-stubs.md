---
category: Fixed
---

- **Fixed:** `scaffold_test_preview` now emits `Substitute.For<T>()` stubs (when NSubstitute is referenced) or typed TODO placeholders per ctor parameter instead of the uncompilable bare `new T()` for DI-registered services with required ctor args (`ScaffoldingService`). (`scaffold-test-preview-ctor-arg-stubs`)
