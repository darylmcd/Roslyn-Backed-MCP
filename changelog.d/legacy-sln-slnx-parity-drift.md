---
category: Maintenance
---

- **Maintenance:** Removed the legacy `Roslyn-Backed-MCP.sln`, which had drifted from `RoslynMcp.slnx` (extra unbuilt `samples/**` project entries) with nothing enforcing parity; `RoslynMcp.slnx` remains the sole solution file for build/test/CI.
