---
category: Fixed
---

- **Fixed:** Cross-project and dependency-inversion interface extraction now spell the generated interface by binding to the generated symbol (bare name when it binds, otherwise namespace-qualified, otherwise `global::`), so the source base list and rewritten constructor parameters no longer fail with CS0246 when the source namespace differs from the interface namespace, an alias shadows the namespace, or a same-named interface is already in scope. A consumer project that cannot yet see the interface project gets a project reference. Same-project extraction with `replaceUsages` binds the same way and now rewrites every field in a document (it previously kept only the last), and keyword-named namespaces resolve. Closes `cross-project-interface-reference-binding` for the base-list and consumer-parameter sites; the `extract_and_wire_interface_preview` DI-registration text rewrite is tracked separately.
