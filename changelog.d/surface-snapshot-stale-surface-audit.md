---
category: Maintenance
---

- **Maintenance:** the `.ai-doc-audit.md` Live Surface snapshot is now gated instead of hand-maintained. `eng/verify-surface-snapshot-freshness.ps1` compares that table against the README live-surface paragraph (CI-guarded by `ReadmeSurfaceCountTests` against `ServerSurfaceCatalog`) and the shipped `skills/` directory, and `/release-cut` Step 1 refuses the cut when it drifts. Refreshed the snapshot to the live 4.1.1 surface (174 tools / 14 resources / 20 prompts / 32 skills; it had been stale at 168/13 since 2026-05-06, across twelve tagged releases) and corrected `docs/setup.md`, which listed 31 shipped skills and omitted `mcp-server-surface-test`. Closes `surface-snapshot-stale-surface-audit`.
