---
category: Fixed
---

- **Fixed:** `test_discover` reporting incorrect `fullyQualifiedName` for test classes declared inside a sub-namespace (folder-infix drift). Previously the FQDN used the MSBuild project name as its namespace prefix; it now uses the class's actual declared namespace. Passing a `test_discover` FQDN as a `test_run --filter` expression will no longer produce silent zero-hits when the project name and namespace diverge. Closes gh #752.
