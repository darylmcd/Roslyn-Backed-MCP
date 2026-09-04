---
category: Fixed
---

- **Fixed:** Drive NuGet version-check timeouts with a test-owned virtual clock and derive completion guards from the configured timeout, eliminating hosted pending-versus-timeout races. Closes `nuget-checker-timeout-test-bound-couple-to-httptimeout`.
