---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Cross-project interface extraction, dependency inversion and extract-and-wire now add valid acyclic project references needed by emitted signature types, preserving aliases and embedded-interop metadata. An existing receiving-project reference with incompatible embedded-interop metadata is refused before a preview token is issued. Migration: align the receiving project's reference metadata with the source project's reference, or choose a compatible receiving project, then preview again. See ADR 0035. Part of `cross-project-interface-signature-reference-closure`.
