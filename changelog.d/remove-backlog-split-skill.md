---
category: Maintenance
---

- **Maintenance:** Removed the `backlog-split` skill. `/doc-audit` no longer caps `ai_docs/backlog.md` by token size or recommends splitting it into `backlog-p3.md` / `backlog-p4.md` (the doc-audit STANDARD now exempts `backlog.md` from size caps). Backlog size is volatile — it swings with the discovery→remediation cadence — and the actionable signal is the **row count** (`/backlog-count`, surfaced in `/backlog-sweep:status`), not byte size. Single-file backlog is now the invariant, so every backlog consumer reads one path with no split-file handling.
