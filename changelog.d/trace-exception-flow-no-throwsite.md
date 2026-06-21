---
category: Fixed
---

- **Fixed:** `trace_exception_flow` now returns throw sites (with a syntactic unhandled-at-boundary flag) alongside catch sites, ranks type-specific catches above base-`Exception` catches so truncation never drops precise handlers, and exposes a `countOmitted` field reporting how many catch+throw sites were clipped by `maxResults`.
