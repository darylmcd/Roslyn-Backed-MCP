---
category: Fixed
---

- **Fixed:** Raised `ValidationServiceOptions.VulnerabilityScanTimeout`'s default from 2 to 5 minutes. `dotnet list package --vulnerable` fetches full package registration + vulnerability metadata from nuget.org (heavier than a normal restore); under a cold per-account NuGet HTTP cache (e.g. a CI service account with a separate profile from an interactive dev session), 2 minutes proved too tight and caused intermittent `NuGetVulnerabilityScanIntegrationTests` timeouts unrelated to any code change. Still configurable via `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS`.
