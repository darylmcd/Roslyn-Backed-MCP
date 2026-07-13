---
category: Fixed
---

- **Fixed:** Reduced hot-path cost in the read-only tool auto-discovery/auto-resolve dispatch path (`SolutionDiscoveryHelper`, `StructuredCallToolFilter`) — root-directory solution scans are now memoized with a short (~10s) TTL, the file-anchored ancestor walk-up is depth-capped (8 hops) and off-loaded off the async continuation thread, and `workspaceId` eligibility checks against `ServerSurfaceCatalog.Tools` are now O(1) via a name-keyed index instead of an O(n) linear scan per call. Closes `solutiondiscoveryhelper-hotpath-perf`.
