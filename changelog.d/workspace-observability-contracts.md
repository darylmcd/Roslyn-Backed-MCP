---
category: Fixed
---

- **Fixed:** workspace lifecycle observability now isolates Git status from ambient repository overrides, avoids cold support-bundle analyzer passes, keeps informational diagnostics non-blocking, reports nested gate holds as one wall-clock interval, and offers an opt-in compact `workspace_reload` response while preserving its default shape. Closes `validate-recent-git-status-windows-timeout`, `workspace-support-bundle-cold-diagnostics-budget`, `workspace-support-bundle-info-only-verdict`, `workspace-gate-metrics-nested-hold-accounting`, and `workspace-reload-compact-response`.
