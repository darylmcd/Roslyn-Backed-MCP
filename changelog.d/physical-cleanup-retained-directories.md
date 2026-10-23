---
category: Changed
---
- **Changed:** On POSIX, isolated workspace cleanup reclaims owned file contents while retaining directory names and empty skeletons when exact final removal cannot be proved. Fork results report this through CleanupWarnings; a dropped fork does not prove its directory name disappeared. Fresh Windows cleanup ownership is acquired atomically; POSIX ownership requires no-follow acquisition of an empty directory and refuses populated replacements. Staging rebuilds use fresh exclusive coordinates so retained directories cannot change native conditions or globs. Remove retained names only under separately enforced exclusive filesystem ownership. See [ADR 0029](../docs/decisions/0029-explicit-preview-snapshot-authority.md).
