---
category: Changed
---

- **Changed:** BREAKING: Interface extraction now checks the bound nullable shape of each generated method, property, indexer, and event against its source symbol before storing a preview token. A generated contract with a changed nested or containing type annotation, or a member that cannot be mapped after parsing, is refused where it was previously accepted. Migration: handle the public invalid-operation refusal, correct the source signature or receiving compiler policy, and request a fresh preview. The request and response schemas are unchanged. See ADR 0034. Closes `interface-extraction-nullability-context-fidelity`.
