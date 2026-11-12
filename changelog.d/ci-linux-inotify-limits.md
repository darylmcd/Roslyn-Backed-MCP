---
category: Fixed
---

- **Fixed:** Linux validation legs now raise `fs.inotify.max_user_instances` and `fs.inotify.max_user_watches` before the build, so a test shard no longer fails with `IOException: The configured user limit (128) on the number of inotify instances has been reached` when parallel test classes each hold about a dozen file watchers.
