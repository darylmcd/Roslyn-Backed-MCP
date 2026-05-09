---
category: Fixed
---

- **Fixed:** `scaffold_test_preview` now detects when the target type or method is internal-not-visible to the test assembly (no `InternalsVisibleTo`) and emits a warning + `Assert.Inconclusive` placeholder instead of a compile-fragile `new TargetType(...)` / `subject.Method()` call that would fail with `CS0122`. Public targets and `InternalsVisibleTo`-granted internal targets are unaffected; private methods continue to use the existing reflection-scaffold path. Closes `scaffold-test-internal-target-accessibility`.
