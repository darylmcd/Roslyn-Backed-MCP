# ADR 0037 — Preserve interpolated handler argument contracts

- **Status:** Accepted (2026-11-08)
- **Deciders:** repository maintainers
- **Scope:** standalone interface extraction, cross-project extraction, dependency inversion and extract-and-wire previews
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

`InterpolatedStringHandlerArgument` changes how a caller constructs a handler parameter. Interface extraction previously dropped the attribute, so a generated interface could change call binding even when its parameter types matched the source member. Copying the attribute without checking the receiver and forwarded arguments could instead select a different handler constructor after the receiver becomes the interface type.

## Decision

Preserve the attribute only when a compiler-bound probe proves the same call contract. For each projected method or indexer with a handler argument, synthesize Original and Projected calls under the receiving project's parse options. The probe recreates the concrete type's containing generic scopes and the member's generic constraints, passes non-handler arguments with their ref kinds, and supplies an interpolated string for each handler argument. It must bind Original to the captured source member and Projected to the emitted contract member, with the same ordered handler constructors. Any compiler error or binding difference refuses the preview before token storage. The refusal lists every distinct compiler diagnostic id.

## Migration

All four preview routes can now refuse a handler signature that previously yielded a preview with a dropped attribute. Choose a handler constructor that binds identically for the concrete and interface receiver, change the forwarded arguments or interface boundary, and request a fresh preview. The tool names, request and response schemas, and token flow do not change. This publicly observable behavior correction is reserved for a major release under `docs/release-policy.md`.
