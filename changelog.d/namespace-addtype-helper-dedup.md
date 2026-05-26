---
category: Maintenance
---

- **Maintenance:** Extracted duplicated `AddType` namespace-walker from `CrossProjectRefactoringService` and `InterfaceExtractionService` into a shared `TypeNamespaceWalker.Collect` helper. Closes `namespace-addtype-helper-dedup` from the 2026-05-26 discovery-sweep refactor audit.
