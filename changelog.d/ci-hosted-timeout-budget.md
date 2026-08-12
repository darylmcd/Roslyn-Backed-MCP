---
category: Fixed
---

- **Fixed:** Raised the CI validation job cap from 20 to 30 minutes after the growing test suite began exhausting the old limit on slower GitHub-hosted Dependabot runs despite clean builds and no test assertion failure; the existing per-command integration-test timeouts continue to fail wedged child processes early (`ci-hosted-timeout-budget`).
