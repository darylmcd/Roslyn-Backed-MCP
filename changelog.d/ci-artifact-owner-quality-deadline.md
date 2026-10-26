---
category: Fixed
---

- **Fixed:** Run package auditing and changed-file formatting in a parallel CI quality job with fresh-runner analyzer preparation and fail-closed aggregate validation. Replace post-test NuGet compression in every cache reader with designated explicit writers before validation, after checked main and canonical sample preparation shared with release verification. Preserve restored-license gating before builds and tests; cache hits still perform normal restores.
