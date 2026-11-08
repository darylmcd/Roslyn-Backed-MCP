# ADR 0034 — Verify generated interface nullable contracts

- **Status:** Accepted (2026-11-07)
- **Deciders:** repository maintainers
- **Scope:** interface extraction previews in the source project and across projects
- **Supersedes:** nothing

## Context

Interface extraction projects source members into a generated interface and inserts nullable directives where the receiving project needs them. The existing projection checked nullable context at selected syntax tokens. A type can have the expected context at those tokens while a nested generic argument, containing type argument, array element, or another signature type still binds with a different nullable annotation. The preview could then store a token for a contract that differs from the source.

## Decision

Retain the source symbol with each captured generated member. After the final generated document is bound under the receiving project's compiler policy, pair each emitted declaration with its source by its unique construction annotation and mapped source span. Compare the recursive nullable annotation shape of return, parameter, property, indexer, and event types. Arrays, pointers, function pointers, named type arguments, and containing types contribute children to that shape. A missing member mapping or a different nullable shape refuses the preview before token storage. The check runs whether or not nullable directives were inserted.

## Consequences and migration

All four interface extraction routes can now refuse a preview they previously issued when the generated signature does not preserve the source nullable contract. The tool request and response schemas are unchanged. Consumers should handle the public invalid-operation refusal, correct the source signature or receiving compiler policy, and request a new preview; they must not apply an old token. This behavior correction changes a publicly observable stable tool result and is reserved for a major release under `docs/release-policy.md`.
