# ADR 0046 — Preserve method-owned constructed type identity in symbol handles

- **Status:** Accepted (2026-11-26)
- **Scope:** `symbol-handle-method-type-parameter-roundtrip`
- **Refines:** ADR 0045 and ADR 0041, Rule 1

## Context

A constructed type such as `List<T>` can bind `T` to a generic method. The legacy handle cannot recover that owner. For a source-backed type, the legacy position and display text can match the open declaration and replay a different type parameter with the same name.

## Decision

| Area | Contract |
|---|---|
| Payload | Use an optional `MethodConstructedType` in the private base64 JSON. It is exclusive with `ConstructedType` and `MemberIdentity`. Newly minted method-owned constructions carry `DisplayName` and omit legacy locators. Existing non-method shapes serialize byte for byte as before. Already-issued handles use the same decoder paths. |
| Owner | Identify the logical method slot by containing assembly and type, metadata name, arity, method kind, staticness, calling convention, bound parameter and return types and ref kinds, and assembly-qualified explicit interface targets. A method's own signature type parameters use scoped ordinals. |
| Replay | Resolve the containing type and require one matching method in the same compilation. Re-encode the complete construction for exact comparison. Competing compilations must agree on the owner definition and logical signature. |
| Staleness | Replay survives source movement and compatible rebuilds that preserve the logical slot. An equal CLR assembly identity denotes the same slot across a compatible rebuild. Changes to encoded assembly identity or bound signature, ambiguity, and missing owners give NotFound. |
| Bounds | Keep ADR 0045's eight-node depth cap. Local, anonymous, reduced or constructed method owners and unsupported signature shapes (including custom modifiers, pointers and function pointers) yield newly minted display-only handles. A non-method construction that exceeds the cap keeps its previous bytes. |
| Downgrade | Older servers ignore the new field. New method-containing handles carry no legacy lookup locator, so older servers return the existing NotFound result even when an open type shares the display name. Obtain a fresh handle on an upgraded server to replay a supported method construction. |

## Release classification

This Rule 1 minor correction preserves the public opaque string field and existing error categories. The release proof compares both supported protocol eras. It checks equivalent results when an already-issued token is replayed before and after the change, legacy byte identity, successful replay with a freshly minted supported token, and NotFound with a fresh display-only token for an unsupported owner. The producer emits the different token and the consumer still accepts the original token with its prior behavior.

## Remainder

Local-function owners need a lexical declaration identity that cannot rebind to a same-signature sibling. Track that separately as `symbol-handle-local-function-owner-roundtrip`.
