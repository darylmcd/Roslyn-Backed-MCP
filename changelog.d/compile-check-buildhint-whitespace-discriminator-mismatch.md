---
category: Fixed
---

- **Fixed:** `compile_check`'s `BuildHint` zero-projects discriminator now uses `IsNullOrWhiteSpace` instead of a bare null check, matching `ComputeRequestedScope` and `ResolveProjectScope` — a whitespace-only `projectFilter` now correctly reports the workspace-reload hint instead of the misleading "did not match any project" wording (`compile-check-buildhint-whitespace-discriminator-mismatch`).
