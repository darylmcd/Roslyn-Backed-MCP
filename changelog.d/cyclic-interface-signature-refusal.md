---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Cross-project interface extraction, dependency inversion and extract-and-wire refuse signatures whose type owner would create a project-reference cycle. The refusal names the type, owner and receiving project before a preview token is issued. Migration: move the type to an independent project, choose an acyclic receiver, or use a signature type already available to the receiver. See ADR 0036. Part of `cross-project-interface-signature-refusal-compile`.
