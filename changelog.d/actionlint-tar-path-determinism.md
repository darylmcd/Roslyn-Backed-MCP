---
category: Fixed
---

- **Fixed:** The actionlint gate's archive extraction no longer depends on which `tar` implementation is first on `PATH`. GNU tar read the leading drive letter of an absolute Windows archive path as a remote `host:path` spec and mangled backslashes passed to `-C`, so `verify-actionlint` failed extraction outright on any Windows host where Git's `usr/bin` shadows the bundled bsdtar. Extraction now runs from the archive's own directory with a bare file name and a forward-slash destination, the one form GNU tar and bsdtar both accept.
