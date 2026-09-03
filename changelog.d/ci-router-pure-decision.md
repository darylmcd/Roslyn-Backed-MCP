---
category: Maintenance
---

- **Maintenance:** Extracted the CI validation-topology decision out of an inline, untestable GitHub Actions script step into `eng/resolve-ci-topology.ps1`, a pure function covered by direct table tests instead of substring sentinels over PowerShell source.
