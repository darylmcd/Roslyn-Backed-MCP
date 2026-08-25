---
category: Fixed
---

- **Fixed:** Make persistent composite-preview redemption atomic across hosts with fail-closed abandoned-claim recovery, validate all scripting timing and capacity environment values during startup, and make release verification rebuild only contained current-run outputs while cleaning every dotnet failure path. Closes `persistent-composite-token-atomic-redemption`, `persistent-composite-storage-delete-toctou-idempotence`, `scripting-options-environment-validation`, `verify-release-owned-output-freshness`, and `verify-release-outer-cleanup-on-pretest-failure`.
