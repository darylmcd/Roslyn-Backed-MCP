---
category: Maintenance
---

- **Maintenance:** The CI NuGet vulnerability audit is now **blocking**. `dotnet package list --vulnerable --include-transitive` exits 0 even when CVEs are present, so the `validate` job now captures its output and fails on findings — and on a genuine audit error (unrestored project, NuGet outage, SDK fault) via a `$LASTEXITCODE` guard, rather than failing open. Also passes `-NoCoverage` to the release `verify-release.ps1` run, dropping ~60–90 s of coverage collection the publish path never consumes. Closes `ci-vuln-audit-gating`.
