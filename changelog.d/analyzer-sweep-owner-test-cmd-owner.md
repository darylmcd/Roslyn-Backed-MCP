---
category: Maintenance
---

- **Maintenance:** The analyzer shadow sweep live-owner test now holds the ownership lock with a `cmd.exe` child instead of a PowerShell cold start that exceeded its 15 s ready deadline on loaded hosted Windows runners; test-only, no shipped behavior change.
