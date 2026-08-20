---
category: Fixed
---

- **Fixed:** analyzer shadow-copy loaders are now leased per workspace load — each load gets its own collectible `AssemblyLoadContext` and uniquely-keyed shadow root that workspace close, reload, and host shutdown reclaim exactly once. Long-running hosts no longer accumulate hundreds of orphaned analyzer shadow trees (hundreds of MB) until process exit. Analyzers keep on-disk shadow copies, so `Assembly.Location` and adjacent-resource/native-dependency behavior are unchanged.
