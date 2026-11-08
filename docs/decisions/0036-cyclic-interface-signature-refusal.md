# ADR 0036 — Cyclic interface signature refusal

- **Status:** Accepted (2026-11-08)
- **Deciders:** repository maintainers
- **Scope:** cross-project interface extraction, dependency inversion and extract-and-wire previews
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

An extracted interface may name a type owned by the source project or by another project that already depends on the receiving project. The receiver cannot reference that owner without forming a project-reference cycle. ADR 0035 added acyclic signature references but left these cycles unhandled, allowing a preview of an interface whose signature cannot bind.

## Decision

Refuse a cyclic signature dependency before storing a preview token. The public refusal names the emitted type, its owning project and the receiving project. This applies to direct cross-project extraction, dependency inversion and extract-and-wire. Types already owned by the receiver require no new edge and remain supported. Other generated-interface compilation diagnostics belong to the dependent compile-gate slice.

## Migration

A caller receiving this refusal should move the signature type to an independent project that both source and receiver can reference, select a receiver that can reference its owner without a cycle, or change the interface signature to use a type already available to the receiver. The tool names, request and response shapes and preview/apply token flow do not change.

## Verification

Regression tests cover source-owned and transitive cycles through all three cross-project routes, with no preview token or workspace/file mutation on refusal. Existing successful fixtures use acyclic signature owners while retaining their formatting, namespace, consumer rewrite and registration assertions.
