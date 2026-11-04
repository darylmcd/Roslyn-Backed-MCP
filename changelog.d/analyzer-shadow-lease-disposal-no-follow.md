---
category: Fixed
---

- **Fixed:** Analyzer shadow lease disposal and the abandoned-root sweep now delete through a shared no-follow walker (`NoFollowDirectoryDeleter`) that removes junctions and symlinks as links, clears read-only attributes, and holds each directory open on Windows while enumerating it. A descendant junction no longer leaks a swept root or risks an external target, and read-only shadow files no longer strand a disposed lease. Closes `analyzer-shadow-lease-disposal-no-follow`.
