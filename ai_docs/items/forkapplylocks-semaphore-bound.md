# forkapplylocks-semaphore-bound — Bound/evict ForkApplyLocks semaphore map in ValidationBundleTools

**row:** `forkapplylocks-semaphore-bound` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:41`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:162`

## Acceptance

- [ ] `ForkApplyLocks` entries are either evicted when idle or documented as intentionally process-lifetime; `SemaphoreSlim` instances are disposed if eviction is added
- [ ] No behavior change to the serialization guarantee for concurrent same-root fork-apply calls

## Evidence

- Static `ConcurrentDictionary<string, SemaphoreSlim>` accumulates one undisposed semaphore per distinct source root with no removal path; bounded in practice (few source roots) but unbounded in principle — see code-quality review, PR #1037.
