# ADR 0035 — Interface signature reference closure

- **Status:** Accepted (2026-11-07)
- **Deciders:** repository maintainers
- **Scope:** cross-project interface extraction, dependency inversion and extract-and-wire previews
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

An extracted interface can name types owned by a third project. The preview previously copied namespace imports and added a reference from the source project to the interface project, but it did not give the receiving project a reference to the signature type's owner. An aliased source reference also needs its alias in the receiving project and a matching `extern alias` directive in the generated file. A namespace or assembly simple name is insufficient to identify the owner.

## Decision

- Resolve each emitted signature type to its source project by its Roslyn symbol and declaring document. Traverse generic arguments, containing types, arrays, pointers and function pointers, plus member constraints and contract attribute types.
- Add a receiving-project reference only when the owner is distinct and the edge is acyclic. Copy the source reference's aliases and `EmbedInteropTypes`; preserve existing receiving-reference metadata. Carry required `extern alias` directives into the generated interface.
- Refuse a valid acyclic extraction before issuing a token when an existing receiving-project reference has incompatible embedded-interop metadata. A blind replacement could change the identity of types already used by that project.
- Keep source-owned and other cyclic signature behavior for the dependent refusal slice. A later compile-gate slice will reject remaining generated-interface errors.

## Migration

Clients receiving the new metadata refusal should align the receiving project's reference metadata with the source project's reference, or extract into a project with compatible references, then request a fresh preview. Ordinary valid acyclic extractions now gain the required dependency automatically. The public tool names, request and response shapes, and preview/apply token flow do not change.

## Verification

Regression tests cover direct extraction, dependency inversion and extract-and-wire with an independent signature owner, plus an alias-only project reference through preview and apply.
