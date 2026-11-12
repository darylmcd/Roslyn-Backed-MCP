---
category: Fixed
---

- **Fixed:** The ubuntu-26.04 hosted image no longer ships the `fs.inotify.max_user_instances=1280` / `fs.inotify.max_user_watches=655360` limits of the 24.04 image, so Linux validation legs now raise `fs.inotify.max_user_instances` and `fs.inotify.max_user_watches` before the build, so a test shard no longer fails with `IOException: The configured user limit (128) on the number of inotify instances has been reached` when parallel test classes each hold about a dozen file watchers.
