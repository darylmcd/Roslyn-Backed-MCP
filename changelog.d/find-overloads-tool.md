---
category: Added
---

- **Added:** `find_overloads` tool — enumerate every overload of a member by type + name via
  `GetTypeByMetadataName`/`GetMembers`, including methods only reachable through a referenced
  BCL/NuGet assembly that `symbol_search` cannot find (it only searches workspace source). Ships
  experimental; extension methods are out of scope for v1.
