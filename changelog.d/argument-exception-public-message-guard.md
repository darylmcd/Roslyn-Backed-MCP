---
category: Maintenance
---

- **Maintenance:** Production code (`src/**`) can no longer construct `ArgumentException` or `ArgumentOutOfRangeException` directly; a banned-symbols rule (`src/BannedSymbols.txt`, `src/Directory.Build.props`) fails the build with RS0030 and points to `PublicArgumentException`, `PublicArgumentOutOfRangeException` or `ArgumentErrors.Redacted`, so a caller-facing refusal cannot silently fall back to the generic redacted message. Closes `argument-exception-public-message-guard`.
