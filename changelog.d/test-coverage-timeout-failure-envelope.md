---
category: Fixed
---

- **Fixed:** `test_coverage` now returns a structured `failureEnvelope` (`errorKind=Timeout` or `errorKind=Unknown`) when the dotnet test run is cancelled or encounters an unexpected runner error, instead of surfacing a bare invocation exception to the MCP host.
