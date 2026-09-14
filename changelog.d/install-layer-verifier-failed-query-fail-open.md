---
category: Fixed
---

- **Fixed:** `eng/verify-install-layers.ps1` no longer reports an installed Layer 1 global tool as "is not installed" when the `dotnet tool list --global` query itself fails. The query's exit code is now checked and an exception is no longer swallowed into an empty result, so a broken `dotnet` resolution (for example a user-local SDK on `PATH` ahead of the one `global.json` pins) is reported as an unverifiable layer naming the real failure, rather than as a false absence that sends the maintainer to run `just tool-update` — which cannot fix SDK resolution. Genuine absence is still reported as before.
