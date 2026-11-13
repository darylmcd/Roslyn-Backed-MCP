---
category: Changed
---

- **Changed:** The four hosted `linux-N-of-4` validation legs now have a 45-minute job cap (was 30), matching the Windows legs. The `linux-2` shard grew to about 28 minutes of tests on the ubuntu-26.04 image (new `UndoServiceTests`, `ForkNativeAdmissionTests` now running to completion) and was cancelled at the old cap with 1601 tests passed. Temporary: the cap returns to 30 minutes once the ubuntu duration profile is refreshed and the shards are rebalanced.
