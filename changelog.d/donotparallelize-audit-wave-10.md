---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `FindOverridesCorlibSuppressionTests`, `FindPropertyWritesHintLocatorShapeTests`, and `FindPropertyWritesPositionalRecordTests` — the first only reads through the synchronized `WorkspaceIdCache`, and the other two load and close their own isolated sample-solution copies, so the opt-out was removed from all three, proven safe by repeated concurrent test runs.
