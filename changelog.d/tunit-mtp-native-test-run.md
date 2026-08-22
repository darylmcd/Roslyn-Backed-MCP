---
category: Fixed
---

- **Fixed:** `test_run` can now execute TUnit test projects. TUnit is entirely built on Microsoft.Testing.Platform (MTP) and never registers with the classic VSTest adapter, so `--logger trx`/`--filter` were silently ignored for it. `test_run` now detects an MTP-only project and invokes it with the MTP-native argument shape (`--report-trx --results-directory <dir>`), producing the same TRX schema the existing result parser already understands. On the .NET 10 SDK the legacy VSTest-mode MTP bridge is hard-removed, so this requires the target repo's `global.json` to opt into the native mode (`"test": {"runner": "Microsoft.Testing.Platform"}`) — `test_run` throws an actionable error naming this when it's missing. A caller-supplied `--filter` also throws for now: MTP's `--treenode-filter` syntax isn't translated from `test_run`'s VSTest-style filter yet.
