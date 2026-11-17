---
category: Maintenance
---

- **Maintenance:** Policy-only documentation pull requests now run one hosted Linux leg with a 30-minute cap (previously four 45-minute shards of the whole suite) that keeps every release gate and runs only the test classes listed in `eng/docs-tier-test-consumers.json` as reading docs-tier files; `DocsTierTestConsumerContractTests` requires every test class with a detectable docs-tier signal to be classified there and reviewed, and `CI_POLICY.md` § Docs Route Test Selection states what that guard cannot detect and the resulting exposure. Markdown under `src/`, `analyzers/`, `samples/` and `tests/` now routes as code. Code pull requests, merge-queue groups and dispatch/schedule runs still run the whole suite.
