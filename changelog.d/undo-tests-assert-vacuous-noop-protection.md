---
category: Maintenance
---

- **Maintenance:** Hardened the three byte-fidelity undo regression tests (added in PR #1144) to assert the apply actually mutated the file before revert, closing a vacuous-pass gap where a future no-op regression on the undo path would have left them green.
