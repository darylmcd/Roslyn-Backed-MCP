---
category: Maintenance
---

- **Maintenance:** Updated the MCP C# SDK from 2.2.0 to 2.3.0 after a dedicated release, API, dependency, license, and dual-era wire review. RoslynMcp remains stdio-only and keeps advertising its server identity; the SDK's nullable client `ServerInfo` and registration-order listing corrections need no RoslynMcp change, its HTTP/SSE/OAuth changes are not adopted, and clients require no migration. ADR 0031 records the disposition and moves ADR 0007's planned Tasks extension pair to 2.3.0.
