---
category: Fixed
---

- **Fixed:** `/mcp-server-surface-test` skill drifted from the live server and plugin packaging: the pre-load gate now accepts `connection.state: idle` (the server stays `idle` until `workspace_load`), `symbol_refactor_preview` tokens are applied with `apply_composite`, `get_test_coverage_map` is described as the `test_coverage` alias, and `agents/audit-phase-runner.md` now ships in the plugin package (`agents/**` allowlist entry + `plugin.json` `agents`). Closes `surface-test-skill-prompt-drift`.
