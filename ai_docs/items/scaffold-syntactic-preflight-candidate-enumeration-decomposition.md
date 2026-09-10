# scaffold-syntactic-preflight-candidate-enumeration-decomposition — Separate syntactic preflight candidate enumeration

**row:** `scaffold-syntactic-preflight-candidate-enumeration-decomposition` · **pri:** `Low` · **size:** `S` · **deps:** `scaffold-fqn-target-type-disambiguation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`

## Acceptance

- [ ] Project traversal, syntax enumeration, qualified-name projection, and ambiguity policy have focused ownership rather than one mixed preflight method.
- [ ] Simple and fully qualified duplicate-type requests keep the same syntactic candidate behavior as semantic resolution.
- [ ] Replay/sampling paths preserve their current qualification-aware result.

## Regression

Exercise a duplicate simple-name fixture through simple-name rejection, fully qualified selection, and sampling replay, proving the syntactic preflight candidates remain equivalent to semantic resolution.
