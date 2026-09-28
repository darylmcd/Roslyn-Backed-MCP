# parameter-object-argument-refusals-public-message — All 28 parameter_object_preview refusals reach the caller verbatim as PublicArgumentException with a real parameter name

**row:** `parameter-object-argument-refusals-public-message` · **pri:** `Medium` · **size:** `S` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] parameter_object_preview on an override returns the 'refuses: ... is an override' text through the envelope.
- [ ] MissingProjectReferenceMessage sites (:990, :1056) carry a paramName.
- [ ] No message embeds an absolute path.

## Evidence

- 28 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~45000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
