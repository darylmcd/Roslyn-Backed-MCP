---
category: Maintenance
---

- **Maintenance:** Single-sourced the `TryElicitChoiceAsync` XML doc — previously byte-identical across three files, one of which still claimed ownership of a body that moved to `ElicitationChoicePrompt` in PR #1205 — by deleting the two traffic-free forwarders in `StructuredCallToolFilter` and `StructuredCallElicitationCoordinator`. Also corrected the `returns` contract, which still claimed cancellation yields `null` after `elicitation-trychoice-cancellation-swallow` changed it to propagate `OperationCanceledException`, and de-hardcoded the namespace-cycle guard test's method name from the layering-invariant doc. All members are `internal`; no published-surface change. (elicitation-doc-drift-and-delegate-chain, part 2 of 2)
