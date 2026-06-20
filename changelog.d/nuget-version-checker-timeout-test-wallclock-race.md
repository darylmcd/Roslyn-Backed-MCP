---
category: Maintenance
---

- **Maintenance:** Fixed a release-gating flake in `NuGetVersionCheckerTests.GetLatestVersion_OnTimeout_...` — the shared `WaitForCompletionAsync` helper's hang-guard bound (`pending.WaitAsync`) was 5 s, only ~1.6× the checker's 3 s internal `HttpTimeout`, so under self-hosted-runner CPU contention the guard could expire before the background check recorded `TimedOut` and threw `TimeoutException`. Raised the bound to 30 s (~10× the internal timeout) so it only trips on a genuine hang, with a load-bearing comment to prevent a third re-tightening recurrence (`nuget-version-checker-timeout-test-wallclock-race`).
