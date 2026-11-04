---
category: Fixed
---

- **Fixed:** Analyzer shadow lease disposal and the abandoned-root sweep now delete through a shared no-follow walker (`NoFollowDirectoryDeleter`). On Windows every entry is opened as a link (never its target), classified and deleted through that same handle, and each directory stays held open until its own postorder delete, so no queued child path can be redirected through a renamed-away parent or a planted junction. Win32 calls use `\\?\` paths. A descendant junction no longer leaks a swept root or risks an external target, and read-only shadow files no longer strand a disposed lease. Platform gap: off Windows the walk is path-based with no hold (a directory swapped to a symlink between classification and enumeration is enumerated through it); an fd-based `openat`/`unlinkat` walk is not implemented. Closes `analyzer-shadow-lease-disposal-no-follow`.
