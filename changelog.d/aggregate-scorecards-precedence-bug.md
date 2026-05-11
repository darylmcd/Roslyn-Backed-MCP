---
category: Fixed
---

- **Fixed:** `eng/aggregate-promotion-scorecards.ps1` no longer crashes on scorecards missing the top-level `scorecard` property. A PowerShell operator-precedence bug — `-not $parsed.PSObject.Properties.Name -contains 'scorecard'` evaluated as `(-not <array>) -contains 'scorecard'` instead of `-not (<array> -contains 'scorecard')` — caused the guard to silently fail and the next line to throw under `Set-StrictMode`. Wrap the right operand of `-not` in parentheses. Surfaced on 2026-05-11 when `process-audit-reports.ps1` Step 2 invoked the aggregate over the maintainer's mixed-format scorecard set.
