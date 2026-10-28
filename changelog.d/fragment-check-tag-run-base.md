---
category: Fixed
---

- **Fixed:** The changelog fragment check now judges a tag-push run against the tagged commit's first parent instead of `origin/main`, so a release cut from a maintenance branch is no longer rejected as change-bearing work that lacks a fragment. Hitchhiking source changes in the release commit are still rejected.
