---
category: Fixed
---

- **Fixed:** `eng/aggregate-promotion-scorecards.ps1` no longer double-counts the hub repo under `-IncludeSelf`. The repo was added explicitly by `-IncludeSelf` **and** re-discovered as a sibling (the self folder sits under the default sibling parent), so it appeared twice in `siblingReposScanned`/`siblingReposWithScorecard` and contributed two votes — a double-counted hub vote could spuriously satisfy the 2-vote promote quorum. Self is now unconditionally excluded from sibling-discovery; the explicit `-IncludeSelf` add is the sole self-inclusion path, so the hub is counted exactly once. Added a regression test asserting the repo appears exactly once with `-IncludeSelf` + a same-named decoy sibling. Closes `aggregate-scorecard-includeself-double-count`.
