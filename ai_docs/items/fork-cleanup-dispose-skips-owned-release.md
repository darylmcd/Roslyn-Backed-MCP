# fork-cleanup-dispose-skips-owned-release — Release both cleanup reservations after disposal failure

**row:** `fork-cleanup-dispose-skips-owned-release` · **pri:** `Medium` · **size:** `S` · **deps:** `fork-admission-dispose-lock-release`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs` (`CleanupFork`)
- focused fork cleanup lifecycle tests

## Acceptance

- [ ] `CleanupFork` attempts both storage deletion reservation and fork ownership disposal when either disposal throws, preserving primary and cleanup failures without hiding uncertain ownership.
- [ ] Red-first regressions exercise each throwing disposer and the case where both throw, verifying each owned release is attempted exactly once.
- [ ] Fork retention and deletion decisions remain tied to the existing ownership checks.

## Evidence

- Cold plan review of `fork-admission-dispose-lock-release` found `CleanupFork`'s `finally` disposes `deletion` before `ownership`; a throwing first disposal skips the second. This is a distinct cleanup ownership path, sequenced after the shared fork lock release work.
