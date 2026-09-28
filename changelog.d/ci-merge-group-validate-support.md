---
category: Maintenance
---
- **Maintenance:** CI now runs on GitHub merge-queue groups (`merge_group`) and reports the required `validate` check for them, routing every queued group to the full code-PR topology so pull requests can land through a merge queue. (`ci-merge-group-validate-support`)
