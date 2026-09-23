---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `AliasToolsTests`, `AnalysisToolsTests`, and `AnalyzerInfoToolsTests` — all three classes only read through the already-synchronized `WorkspaceIdCache`/read-only services with no shared mutable state, so the opt-out was removed from each, proven safe by repeated concurrent test runs.
