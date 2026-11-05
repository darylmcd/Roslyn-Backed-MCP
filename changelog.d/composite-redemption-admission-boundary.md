---
category: Fixed
---

- **Fixed:** `apply_composite` and `apply_project_mutation` now revalidate their write set against the configured sanctioned-root boundary at redemption time, under the write gate and before a persisted composite token is claimed (the check every other `*_apply` route already made), and fail closed with a stale-token refusal when the persisted composite record is missing, already claimed or unreadable; a persisted record edited to name an out-of-root path is refused with the token intact, persisted records missing required members are rejected, and direct composite orchestration for a session the host never admitted is refused before the first write instead of after it. Closes `composite-redemption-admission-boundary`.
