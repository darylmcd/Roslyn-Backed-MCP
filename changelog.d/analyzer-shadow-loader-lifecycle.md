---
category: Fixed
---

- **Fixed:** Analyzer shadow-copy loaders are now leased per workspace with a reclaimable shadow root, so a long-lived server no longer accumulates analyzer `AssemblyLoadContext` instances and their shadow directories across repeated workspace loads. Analyzer `Assembly.Location` and adjacent-resource probing behave as before — the lease reclaims a shadow root only once no workspace holds it. Closes `analyzer-shadow-loader-lifecycle`.
