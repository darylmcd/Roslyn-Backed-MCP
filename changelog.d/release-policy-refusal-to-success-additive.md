---
category: Changed
---

- **Changed:** Release policy: a stable tool that starts accepting input it previously refused, with an unchanged success shape, is additive and ships in a minor as `Added`; the removed refusal text is not a contract. Migration: clients that branched on a refusal message for that input now receive a normal result.
