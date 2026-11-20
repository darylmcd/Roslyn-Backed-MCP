---
category: Maintenance
---

- **Maintenance:** `eng/aggregate-promotion-scorecards.ps1` and `eng/propose-promotion-scorecard-backlog-rows.ps1` now emit UTF-8 stdout, and the pwsh-spawning test helpers (`PwshScriptRunner` and the scorecard script test runner) decode child output as UTF-8, so non-ASCII scorecard text such as arrows no longer becomes 0x1A (invalid JSON) under the Windows OEM codepage.
