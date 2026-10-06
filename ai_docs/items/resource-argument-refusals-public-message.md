# resource-argument-refusals-public-message — Resource-path argument refusals (file/lines resources, server resource slots, catalog diff) are Public and path-free

**row:** `resource-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs`
- `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceResourceTests.cs`

## Acceptance

- [ ] Malformed lineRange names the format without echoing input.
- [ ] A non-absolute filePath refusal no longer embeds the received path.
- [ ] An unsupported catalog diff lists supported pairs without echoing versions; WorkspaceResources.cs:198 no longer interpolates ex.Message.

- [ ] Catalog-diff resource and target-parameter descriptions promise only accepted current-version targets and aliases; metadata regressions reject stale fixed target examples.

## Evidence

- 10 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

- 2026-10-04: ServerResources.cs catalog-diff resource/target descriptions still advertise 2.3.2. ServerSurfaceCatalog.CreateVersionDiff accepts only V231ReleaseVersion -> CurrentReleaseVersion; Directory.Build.props currently sets 4.3.0. Keep guidance version-neutral and test it with the accepted aliases. Same catalog-version correction mechanism and existing scoped files.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~40000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
