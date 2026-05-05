---
category: Added
---

- **Added:** stage-fine progress emission for `workspace_load`, `workspace_warm`, `build_workspace`, and `test_run`. The four long-running tools now emit intermediate stage labels (e.g. `validating-path → opening-workspace → checking-restore → done` for `workspace_load`) instead of waiting silently between the initial 0% and final 100% notification. Stage labels are kebab-case and stable across releases — clients may key UI strings off them. Per-project N/M counts are intentionally not emitted at this layer (would require service-interface changes past the audit-coverage scope). The `ProgressHelper.ReportStage` helper additively complements the existing `Report` overload; no breaking changes to existing callers. Closes `progress-emit-audit-coverage`.
