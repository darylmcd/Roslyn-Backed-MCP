---
category: Changed
---

- **Changed:** `/release-cut` Step 6 now refreshes **both** install layers instead of treating the Layer 1 global tool as optional, and proves it with the new `eng/verify-install-layers.ps1` (Layer 1 read from `dotnet tool list --global`, Layer 2 from the plugin cache and its cached `plugin.json`). The layers drift in both directions — v1.29.0 and v1.34.2 left Layer 2 stale, v4.1.2 left Layer 1 a release behind — so the verifier is the step's completion check and the Step 6 checkpoint probe. The maintainer `/update` skill's global-tool step is likewise required rather than optional, and documents the Windows tool-store lock: identify the holder by image path under `~/.dotnet/tools/`, never by the `roslynmcp.exe` image name, since the plugin's `dnx`-launched Layer 2 server shares it.
