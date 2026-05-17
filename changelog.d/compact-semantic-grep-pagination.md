---
category: Fixed
---

- **Fixed:** `semantic_grep` response envelope now includes `offset`, `totalCount`, and `hasMore` fields and accepts an `offset` parameter, enabling callers to page past the first 500-hit window on broad queries. Decouples the 500-hit collection ceiling from the user-facing page `limit` so pagination actually advances. Mirrors the pagination contract established by `find_reflection_usages` and `find_type_usages`. Closes `compact-semantic-grep-pagination` (split from gh #760, companion to PR #780).
