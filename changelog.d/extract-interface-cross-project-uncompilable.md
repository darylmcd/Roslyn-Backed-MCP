---
category: Fixed
---

- **Fixed:** `extract_interface_cross_project_preview` no longer generates an uncompilable interface file when the source type's method signatures reference types from a sibling namespace inside the source project. The generated interface now emits the required `using` directives via the same semantic-walker approach (`CollectReferencedNamespaces` + `BuildUsingDirectives`) already used by `extract_interface_preview` for same-project extraction. Replaces the legacy text-grep `FilterUsingsForMember` filter that compared each using's last namespace segment against `MinimallyQualifiedFormat` short names and silently dropped source-project usings whose last segment didn't match. Also fixes the same regression on the DI-inversion path (`dependency_inversion_preview`), which routes through the same code path. Closes `extract-interface-cross-project-uncompilable` (gh #765).
