---
category: Maintenance
---

- **Maintenance:** Collapsed the `HasElicitation` forwarder chain — the predicate now has a single definition on `ElicitationChoicePrompt`, with the `StructuredCallToolFilter` and `ElicitationAllowlistPolicy` thin delegates (and their duplicated doc blocks) deleted and the one remaining production caller in `StructuredCallElicitationCoordinator` retargeted at the canonical member. All members are `internal`; no published-surface change. (elicitation-doc-drift-and-delegate-chain, part 1 of 2)
