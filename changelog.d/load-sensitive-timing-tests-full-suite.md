---
category: Fixed
---

- **Fixed:** Made load-sensitive timing tests deterministic: `WorkspaceManager` accepts an injectable `TimeProvider` for missing-workspace retirement retries (the retry test now drives a `FakeTimeProvider` instead of a real timer), and the warm-cache test asserts the structural zero-cold-compilation result instead of a 10x wall-clock ratio. Closes `load-sensitive-timing-tests-full-suite`.
