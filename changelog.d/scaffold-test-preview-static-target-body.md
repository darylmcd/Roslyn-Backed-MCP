---
category: Fixed
---

- **Fixed:** `scaffold_test_preview` now detects static target types whose public surface is constants-only or static-properties-only (e.g. `TenantConstants`, `SnapshotContentHasher`) and scaffolds the utility-shaped body instead of emitting `var subject = new TargetType();` that fails to compile on ctor-less types — `ShouldUseStaticTestScaffold` now considers fields/properties/events alongside methods (`ScaffoldingService`). (`scaffold-test-preview-static-target-body`)
