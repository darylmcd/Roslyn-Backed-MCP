---
category: Maintenance
---

- **Maintenance:** Policy-only documentation pull requests now run one hosted Linux leg with a 30-minute cap (previously four 45-minute shards of the whole suite) that keeps every release gate and runs only the test classes that read docs-tier files, listed in `eng/docs-tier-test-consumers.json` and kept complete by `DocsTierTestConsumerContractTests`; Markdown under `src/`, `analyzers/`, `samples/` and `tests/` now routes as code. Code pull requests, merge-queue groups and dispatch/schedule runs still run the whole suite.
