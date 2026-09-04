# ADR 0009 — Tool-surface consolidation and alias lifecycle

- **Status:** Accepted (2026-09-04)
- **Deciders:** repository maintainers
- **Scope:** public MCP tool consolidation, preview/apply routing, and deprecated tool aliases
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

RoslynMcp exposes related tools whose names or workflows can overlap. Consolidation can improve
discovery, but apply routes carry materially different mutation risks. A shared response notice for
deprecated aliases does not by itself define how long aliases remain callable or when they may be
removed from the published surface.

## Decision

1. Classify every preview/apply family into exactly one risk bucket.
2. Permit consolidation within one bucket when the canonical route preserves token provenance,
   validation, refusal behavior, recovery guidance, and mutation semantics.
3. Prohibit apply-route consolidation across risk buckets. Similar names or payload shapes are not
   evidence that mutation contracts are interchangeable.
4. Keep each deprecated alias callable for at least one released minor version after its deprecation
   first ships. Remove it only in the next major version, with a migration note naming its canonical
   replacement.
5. Preserve alias responses as explicit compatibility paths: identify the canonical tool and retain
   the same authorization, validation, error, and token-provenance boundaries as that route.

## Risk buckets

| Bucket | Mutation boundary | Consolidation constraint |
|---|---|---|
| Formatting | Whitespace, trivia, newline, and layout changes that preserve program semantics. | Merge only with formatting routes that retain the same range and file-boundary validation. |
| Text edit | Caller-supplied textual insertion, replacement, or deletion. | Keep separate from semantic transforms; preserve exact-range, encoding, and stale-snapshot checks. |
| Code transform | Roslyn semantic refactorings and code-fix transformations. | Share apply only when preview-token kind and semantic validation are equivalent. |
| File lifecycle | File creation, move, rename, or deletion. | Preserve path authorization, overwrite refusal, and source/destination lifecycle guarantees. |
| Project file | MSBuild, package, reference, and project-metadata mutation. | Preserve evaluated-project validation, restore implications, and project-file recovery guidance. |

No canonical apply route may redeem preview tokens from more than one row of this table. A composite
workflow may coordinate buckets, but each child mutation must retain its bucket-specific apply
boundary and failure contract.

## Public-change classification

| Change | Version classification | Consumer migration |
|---|---|---|
| Add a canonical tool while retaining callable aliases | Minor | Prefer the canonical name; existing alias calls continue to work. |
| Mark an alias deprecated | Minor | Move calls to the canonical tool during the supported minor window. |
| Remove a deprecated alias after at least one released minor | Major | Replace the removed name with the canonical tool named in release notes. |
| Consolidate apply routes within one risk bucket | Minor only when request/result and mutation contracts remain compatible; otherwise major | Follow any schema or token migration note. |
| Consolidate apply routes across risk buckets | Prohibited | Not applicable. |

## Consumer migration

Deprecation responses and release notes name the canonical replacement. Consumers may migrate at
any point during the mandatory minor-version window. The alias remains executable throughout that
window; warnings are not an early-removal mechanism. A later major release may remove it only after
the changelog records the old name, canonical name, and any request or result differences.

## Consequences

- Tool-count reduction is subordinate to mutation-boundary correctness.
- Alias registries and catalog metadata must make deprecation age and canonical ownership auditable.
- Cross-bucket composite workflows retain distinct child previews and applies.
- Future consolidation PRs cite this decision and identify their bucket before changing a public
  tool or token route.

## Validation

- `eng/verify-ai-docs.ps1` verifies this decision, its required bucket/lifecycle statements, and the
  release-policy cross-reference.
- Surface and alias tests verify callable deprecated routes and canonical replacement notices.
- Preview/apply provenance tests verify that unrelated token kinds are rejected before mutation.
- The complete release gate and hosted `validate` aggregate remain required.

## References

- [Release policy](../release-policy.md)
- [ADR 0003 — MCP SDK 2.x wire compatibility](0003-sdk-2x-wire-compatibility.md)
