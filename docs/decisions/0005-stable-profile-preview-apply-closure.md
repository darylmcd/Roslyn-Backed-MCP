# ADR 0005 — Stable-profile preview/apply closure

- **Status:** Accepted (2026-08-22)
- **Deciders:** repository maintainers
- **Scope:** `ROSLYNMCP_TOOL_TIERS=stable` tool registration and preview-token redemption
- **Supersedes:** the assumption that support-tier filtering alone produces a usable tool graph
- **Superseded by:** nothing

## Context

The stable-only profile previously selected every tool labeled `stable`. Nineteen stable preview
tools issued tokens whose only compatible apply route was labeled `experimental` and therefore not
registered. Those tokens could not be redeemed in the same running server profile. Discovery,
startup counts, server instructions, and direct dispatch all inherited the tier-only selection, so
the unusable previews were publicly callable rather than merely documented incorrectly.

The affected families are move/extract/range refactoring, file lifecycle, dead-code removal,
project mutation, and test scaffolding. The five preview/apply pairs whose two routes are stable
remain available. The default `stable,experimental` profile is unchanged.

Removing callable stable-profile entries is a breaking correction under `docs/release-policy.md`.
The defect is not eligible for a deprecation window: retaining a preview until its apply route is
promoted would continue issuing tokens the selected profile cannot redeem.

## Decision

1. The catalog owns an explicit preview-to-apply route map for token-issuing tools.
2. Tool selection first applies the requested support tiers, then removes any preview whose mapped
   apply route is absent from that selection.
3. Registration, discovery documents, startup diagnostics, server instructions, and wire tests use
   that same closed selection. Direct calls to an omitted preview fail as unavailable.
4. `ROSLYNMCP_TOOL_TIERS=stable,experimental` preserves the complete catalog in its existing order.
5. A stable-only regression requires every retained preview with a declared route to retain its
   compatible apply route, and every stable preview name to declare that relationship.

## Consequences

- The stable-only profile exposes 94 callable tools instead of 113 stable-labeled tools.
- Stable classification remains a compatibility statement about the preview request/response
  contract. Profile availability additionally depends on the token-redemption route.
- Adding a token-issuing preview requires adding its compatible apply route to the centralized map.
- The next release that consumes the accompanying breaking changelog fragment must advance the
  major version.

## Consumer migration

Clients using `ROSLYNMCP_TOOL_TIERS=stable` must rediscover the surface and call only tools returned
by `tools/list`. To use an omitted preview family, enable the default `stable,experimental` profile
and treat its apply route as experimental. Clients must not retain or transfer preview tokens across
server restarts or profiles.
