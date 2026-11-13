---
category: Fixed
---

- **Fixed:** The native late-consumer fork-admission tests for the `CopyUpToDateMarker` consumer (`NativeLateConsumers_CopyMarkerAndManifests_RespectActualHooksAndConditions`, the `marker` and `marker-prior-flags` cases) now request both `_CopyFilesMarkedCopyLocal` and `CopyFilesToOutputDirectory`. SDK 11.0.100 no longer touches the marker inside `_CopyFilesMarkedCopyLocal` (it records the copy there and touches the marker later, in `CopyFilesToOutputDirectory`), so on the ubuntu-26.04 hosted image, which carries SDK 11.0.100 beside 10.0.404, the tests found no marker file and failed on every `linux-2` shard. No product behavior changes.
