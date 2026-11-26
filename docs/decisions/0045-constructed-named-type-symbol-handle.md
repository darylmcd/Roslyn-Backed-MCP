# ADR 0045 — Preserve constructed named-type identity in symbol handles

- **Status:** Accepted (2026-11-26)
- **Scope:** `symbol-handle-constructed-generic-roundtrip`
- **Refines:** ADR 0041, Rule 1

## Context

`symbol_info` can emit a handle at a constructed type use such as `Base<int>`. Its legacy display text does not resolve as a CLR metadata name. A source position identifies `Base<T>` and cannot recover the construction or its argument assembly identities.

## Decision

| Area | Contract |
|---|---|
| Payload | Add optional `ConstructedType` to the private base64 JSON recipe. Preserve byte-for-byte serialization of legacy definitions, unbound types, members, and unsupported shapes. |
| Identity | Encode the exact assembly and outer-to-inner metadata names, per-level arguments, nullability, tuple labels and native integer flags. Support representable array shapes, dynamic and type parameters owned by named types. |
| Bounds | Limit recursive type recipes to eight type nodes per path. Use legacy emission for unsupported or over-budget shapes, including method-owned type parameters (extended by [ADR 0046](0046-method-owned-constructed-type-symbol-handle.md)), rank-one non-SZ arrays, explicit metadata array sizes or lower bounds, and unsupported custom modifiers. |
| Replay | Resolve each recipe within one compilation and compare the reconstructed structure against the original recipe. When more than one compilation matches, require equivalent declarations for the root, containing types and type arguments. Compare source declaration paths and spans or metadata module identities. A valid but stale or ambiguous recipe returns no symbol. A malformed recipe or combined member and constructed type identity uses the existing argument error category. |
| Compatibility | Prefer the constructed recipe when present and never fall through to declaration position. Existing handles without the field use the legacy path. Earlier servers ignore the additive field and retain their legacy behavior. |

## Release classification

Rule 1 qualification requires red-first round trips, byte-identical legacy controls, and raw producer and consumer evidence in both protocol eras. Newly generated supported handles recover the intended construction through the existing opaque string field. Existing legacy handles retain their prior behavior. To obtain the corrected identity for a previously emitted lossy handle, request a fresh handle from the source or metadata binding.

## Remainder

[ADR 0046](0046-method-owned-constructed-type-symbol-handle.md) extends the recipe to supported method-owned type parameters. Local-function owners remain tracked separately.
