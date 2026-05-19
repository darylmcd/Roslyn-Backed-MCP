---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `find_type_mutations`: `MutatingMemberDto.MutationScope` (string, single highest-severity scope) is replaced by `MutationScopes` (`IReadOnlyList<string>`, all detected scopes). Methods performing compound mutations — e.g. both `IO` and `CollectionWrite` — now report every applicable scope rather than only the highest severity. Callers must update from `member.MutationScope == "IO"` to `member.MutationScopes.Contains("IO")`. Fixes gh #741.
