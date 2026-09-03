# scaffold-target-type-ambiguity-before-sampling — Validate target-type ambiguity before sampling

**row:** `scaffold-target-type-ambiguity-before-sampling` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`

## Acceptance

- [ ] A `scaffold_test_preview` whose `targetTypeName` matches types in multiple namespaces returns the ambiguity error without any sampling request reaching the client.
- [ ] A regression asserts the provider's `SuggestTestNameAsync` call count is 0 for the ambiguous-target case.

## Evidence

Traced by the cold reviewer of PR #1428 and confirmed against the merged tree: the sampling hoist
that row `scaffold-sampling-mrtr-replay-cost` shipped moves `SuggestSampledTestNameAsync` ahead of
`ResolveTargetTypeAndMethodAsync`. `FindTargetTypeAsync` throws `InvalidOperationException` when more
than one candidate type matches, so an ambiguous `targetTypeName` now pays a full client sampling
round trip before surfacing the same error it previously surfaced first.

## Context

Deliberately excluded from PR #1428's scope: the hoist itself is correct and is the whole point of
that initiative. Fixing this properly needs a cheap syntactic/no-compilation candidate probe ahead of
the sampling request, which is new work rather than a defect in the shipped diff. The stanza's Risks
row accepted only the loss of the symbol-derived signature/namespace, not this reordering.

[source: 2026-09-02 backlog-remediate PR #1428 cold review]
