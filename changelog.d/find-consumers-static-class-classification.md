# find-consumers-static-class-classification

**Fixed:** `find_consumers` and `find_type_consumers` now classify static-class consumers as `StaticMemberAccess` / `invocation` instead of the uninformative `Other` / `local` buckets. Calls like `AnimalFormatter.Format(...)` are now correctly identified rather than falling through to the catch-all bucket.
