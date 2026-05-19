---
category: Fixed
---

- **Fixed:** `migrate_package_preview` now preserves asset-isolation metadata (`PrivateAssets`, `IncludeAssets`, `ExcludeAssets`, `VersionOverride`) from analyzer-only `PackageReference` entries onto the replacement element; previously these child-element and attribute-form constraints were silently stripped, causing analyzer packages to be rewritten as full runtime dependencies after apply. Fixes gh #753.
