---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `test_run` now paginates its failures array (`failuresOffset`/`failuresLimit`, default limit 25) and head-truncates each failure's `Message` (500 chars) / `StackTrace` (1500 chars) with a visible `"... [truncated]"` marker, mirroring `test_discover`'s existing pagination and `DotnetCommandRunner`'s StdOut/StdErr truncation precedent. Aggregate `total`/`passed`/`failed`/`skipped` counts are never truncated. This changes the default JSON response shape (adds `failuresOffset`/`failuresLimit`/`failuresTotal`/`hasMoreFailures` fields) and may shorten previously-unbounded failure detail for very long assertion messages/stack traces — pass a higher `failuresLimit` or re-fetch with `failuresOffset` to see more.
