---
category: Fixed
---

- **Fixed:** `migrate_package_preview` no longer silently adds a `<PackageVersion>` entry to `Directory.Packages.props` for the replacement package when the source package has no references in any project file. The preview now throws with "No project references to '...' were found", leaving `Directory.Packages.props` unmodified. Closes `migrate-package-preview-no-op-silent-mutation`. Fixes gh #768 §13.17.
