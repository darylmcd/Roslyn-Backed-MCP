# code-action-index-public-refusal — Publish actionable code-action index refusals

**row:** `code-action-index-public-refusal` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs:174`
- `tests/RoslynMcp.Tests/CodeActionServiceTests.cs`
- (new) `tests/RoslynMcp.Tests/CodeActionArgumentRefusalWireTests.cs`

## Acceptance

- [ ] Invalid actionIndex returns an authored public correction naming actionIndex and the available action count through the production tool error envelope; preserve the released InvalidArgument category and ArgumentException identity.
- [ ] Observe a wire regression fail on old behavior and pass after conversion, including zero available actions, negative index and index equal to count; retain valid code-action preview behavior and exclude caller paths, tokens and free text from the complete serialized envelope.

## Evidence

- At main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`, `CodeActionService.cs:174` constructs `new ArgumentException($"Action index {actionIndex} is out of range. Available actions: {actions.Count}")` without ParamName or the public-message contract.

## Context

- Split from `code-action-and-flow-argument-refusals-public-message`: action-index correction is independent of flow-region and FixAll scope validation.
- Publication contract: `src/RoslynMcp.Core/Services/PublicArgumentException.cs`, `PublicArgumentOutOfRangeException.cs`, `ArgumentErrors.cs`; resolve the current host boundary and `docs/release-policy.md` before implementing. The deleted core-move detail is not an authoritative design source.
- backlog: sync ai_docs/backlog.md.
