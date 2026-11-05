---
category: Fixed
---

- **Fixed:** `apply_composite` and `apply_project_mutation` now revalidate their write set against the configured sanctioned-root boundary at redemption time, under the write gate (the check every other `*_apply` route already made), so composite previews touching documents outside the sanctioned roots (linked files) are now refused at apply; a persisted composite token is checked before it is claimed (refused with the token intact, and fail-closed with a stale-token refusal when its record is missing, already claimed or unreadable) and again on the claimed record before the first write, so a record swapped in the shared persist directory between the two, or bound to a different workspace than the one gated, is refused; persisted records missing required members are rejected, and direct composite orchestration for a session the host never admitted is refused before the first write instead of after it. Closes `composite-redemption-admission-boundary`.
