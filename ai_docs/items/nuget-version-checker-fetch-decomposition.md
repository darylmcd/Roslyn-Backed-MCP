# nuget-version-checker-fetch-decomposition — Decompose latest-version fetch orchestration

**row:** `nuget-version-checker-fetch-decomposition` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs:207`
- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs`

## Acceptance

- [ ] Separate HTTP lifetime, response parsing, version selection, and terminal status projection from `FetchLatestVersionAsync`.
- [ ] Reduce `FetchLatestVersionAsync` below CC10 without changing the public non-blocking/cache/status contract.
- [ ] Preserve success, malformed-response, timeout, and transport-failure regressions.

## Evidence

- Current-session touched-file complexity review measured CC12 across 76 lines after the timeout race fix.
