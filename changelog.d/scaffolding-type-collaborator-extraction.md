---
category: Maintenance
---

- **Maintenance:** extracted a `TypeScaffolder` collaborator from `ScaffoldingService`, isolating type-preview interface resolution and member-stub rendering from the god-class facade; `IScaffoldingService`'s public contract and DI lifetime are unchanged. Closes `scaffolding-type-collaborator-extraction`.
