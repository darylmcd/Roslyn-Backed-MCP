---
category: Fixed
---

- **Fixed:** Self-hosted workspace validation now retargets project analyzer references through shadow-copy loaders so loading this repo no longer leaves the server-surface analyzer DLL locked for child build validation. Closes `build-test-self-analyzer-file-lock`.
