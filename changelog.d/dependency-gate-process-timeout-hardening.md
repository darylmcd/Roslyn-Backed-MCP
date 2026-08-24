---
category: Fixed
---

- **Fixed:** Made the NuGet-audit and package-family PowerShell contract tests tolerate loaded Windows process-start latency without serialization, observe MSTest cancellation, and bound process-tree termination, root-process reaping, and redirected-output draining before fixture deletion; changelog validation now also rejects extra body lines instead of silently accepting a second category bullet.
