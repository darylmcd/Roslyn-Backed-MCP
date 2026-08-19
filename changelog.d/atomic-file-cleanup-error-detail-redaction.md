---
category: Fixed
---

- **Fixed:** temp-file cleanup failures in `AtomicFileWriter` no longer log the raw exception or absolute temp/target paths. The best-effort cleanup and the re-thrown primary write failure are unchanged; the warning now carries only a stable cleanup category, the target file name, and the shared secret-safe projection (correlation id, exception-type topology, stack depth).
