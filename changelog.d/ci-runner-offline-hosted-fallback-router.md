---
category: Added
---

- **Added:** CI gains a hosted-fallback router — a cheap `route` job probes the self-hosted runner's online status via the GitHub API and feeds `validate`'s `runs-on` through `needs`-outputs, so maintainer PRs land on `ubuntu-latest` instead of queueing for hours when the self-hosted box is asleep or wedged. A missing or invalid `RUNNER_STATUS_PAT` secret degrades safely to the existing self-hosted routing and never blocks CI (`ci-runner-offline-hosted-fallback-router`).
