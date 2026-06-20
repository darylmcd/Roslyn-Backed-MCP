---
category: Maintenance
---

- **Maintenance:** `.github/dependabot.yml` now groups minor/patch bumps per ecosystem (`github-actions`, `nuget`) into a single weekly PR each, so routine updates no longer open one PR apiece; major bumps stay individual for isolated review (`dependabot-groups-batching`).
