# extract-type-partial-overload-identity — Handle partial method pairs and partial overload ambiguity

**row:** `extract-type-partial-overload-identity` · **pri:** `Medium` · **size:** `S` · **deps:** `type-extraction-service-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1068-1070` — ambiguity check skipped when every candidate is partial.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1165-1184` — helper emitted as non-partial `public sealed class`.
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs:2109` — CS0751 compile-gate case (PartialHostFixture.Hook) pins today's refusal.

## Acceptance

- [ ] Red first: `public partial class P1 { partial void Hook(int x); partial void Hook(int x) { } }`, extract `Hook`: CS0751 in the non-partial helper.
- [ ] Two distinct partial overload pairs with the same name are refused as ambiguous.
- [ ] A single pair either moves as a matching partial pair into a partial helper or is refused (typed).

## Evidence

- 2026-10-27: CS0751 observed on main's rendered helper.

## Context

- From the deleted extraction archive's proposed row of the same name.

## Notes

2026-11-11 remediation: excluded, major. Today the CS0751 compile gate refuses with a PublicInvalidOperationException (public message). Any complete fix replaces it with a typed or ambiguity refusal: ExtractTypeBlockingDependencyException is not IPublicMessageException, so the wire message becomes generic and gains a blockingDependencies field = existing public message change = major (docs/release-policy.md). The alternative (emit a partial helper) conflicts with acceptance bullet 2 (two distinct pairs must be refused). Next-major trigger: ships with type-extraction-service-refusals-public-message (ADR + migration note per docs/release-policy.md Compatibility Policy), or an operator decision that replacing one refusal with another is not breaking.
