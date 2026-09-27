# public-argument-exception-core-move — Move PublicArgumentException to Core and publish Roslyn-layer argument refusals

**row:** `public-argument-exception-core-move` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Services/IPublicMessageException.cs`
- `src/RoslynMcp.Core/Services/PublicArgumentException.cs`
- `src/RoslynMcp.Core/Services/PublicInvalidOperationException.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/ProtocolCompatibility/LspSourceLocationArgumentNormalizer.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ReferenceResponsePager.cs`
- `tests/RoslynMcp.Tests/PublicMessageExceptionTests.cs`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] PublicArgumentException and IPublicMessageException live in RoslynMcp.Core.Services; the Host.Stdio copy is deleted.
- [ ] PublicInvalidOperationException implements IPublicMessageException; ToolErrorHandler checks the marker in both arms.
- [ ] Envelope exceptionType for a marker exception is its BCL base type name.

## Evidence

- analyze_snippet bogus kind → "Parameter '<unknown>' is invalid…"; go_to_definition(line=99999)/enclosing_symbol(line=0) → "Parameter 'line' is invalid…" without the file line count. — mcp-surface-audit 20260924-1305 (check C1).
- `PublicArgumentException` is `internal` to Host.Stdio (`ToolErrorHandler.cs:15`); RoslynMcp.Roslyn cannot reference the host (plan-deepener, backlog-remediate 20260926T234932Z).

## Context

- Split from `argument-exception-throw-sites-lack-public-message` (acceptance bullet 2) during backlog-remediate 20260926T234932Z; that row keeps the Host.Stdio throw sites and the banned-API guard. Host.Stdio users of the type (`ClientRootPathValidator`, `LspSourceLocationArgumentNormalizer`, `ReferenceResponsePager`) need a `using` after the move.
2026-09-26: re-scoped as a child of the argument-error contract redesign.

Over the Rule 3/4 target by a named forcing shape: structural-unit + gate-forced-companion. Structural unit (4): IPublicMessageException, PublicArgumentException, PublicInvalidOperationException (implements marker) and their sole classifier ToolErrorHandler (:154, :580, :679); the marker has no meaning without its consumer. Gate-forced (3): deleting the Host type at ToolErrorHandler.cs:15 makes CS0246 fire at ClientRootPathValidator.cs:156, LspSourceLocationArgumentNormalizer.cs:36/:47 and ReferenceResponsePager.cs:78 (find_references use sites; none import RoslynMcp.Core.Services). StructuredWorkspaceResolver.cs:136/:321 already imports it, so it is not edited.

### Family design (authoritative)

## Invariant

Every ArgumentException-family throw under `src/` that can reach `ToolErrorHandler` is exactly one of:

| Kind | Expressed as | Caller sees |
|---|---|---|
| P: public caller error | `new PublicArgumentException(msg, param)` (Core, implements `IPublicMessageException`) | msg verbatim, category InvalidArgument |
| R: deliberately redacted caller error | `ArgumentErrors.Redacted(param, serverDetail, inner?)`, or a BCL `ThrowIf*` helper / `ArgumentNullException(param)` | `Parameter '<param>' is invalid...` (ParamName always set) |
| I: internal invariant (not a caller error) | `UnreachableException` for impossible arms; `ThrowIf*`/`InvalidOperationException` for wiring/contract; `InvalidOperationException` for env/options config at startup | InternalError / InvalidOperation, never blames the caller |

No message or ParamName sniffing in `BuildSafeArgumentMessage`.

## Decisions

- (a) Core types. `IPublicMessageException { string PublicMessage }` is implemented by `PublicArgumentException` (moved from `ToolErrorHandler.cs:15`, public sealed) and `PublicInvalidOperationException`. The marker is justified by 3 consumers: the argument arm (`:580`), the InvalidOperation arm (`:154`), and exceptionType normalization. There is no `PublicArgumentOutOfRangeException`: range errors use `PublicArgumentException` with the bound in the message.
- (b) Intentional redaction uses the Core factory `ArgumentErrors.Redacted` (own child `argument-errors-redacted-factory`), not a new type. The factory returns an exact `ArgumentException`, so exceptionType stays `ArgumentException` and redacted sites keep their `ThrowsExactly<ArgumentException>` pins (133 pins in 47 test files at HEAD). Once the ban lands it is the only other construction path, so intent is greppable without pragmas. Prefer rewriting a message to be path-free and Public. Use Redacted only when the useful detail IS caller free text (regex, raw token, JSON) or a server path.
- (c) Internal invariants must stop being ArgumentException, because today they classify as InvalidArgument and blame the caller. Sites: HostEnvironmentOptions.cs:20, ServerObservability.cs:24, ToolOutputSchemaIndex.cs:120-143 (4), ToolAliasDeprecation.cs:81, StructuredToolResult.cs:22, WorkspaceTools.cs:153, WorkspaceValidationService.cs:616, FileWatcherService.cs:139, DotnetCommandRunner.cs:162, UndoService.cs:75, SymbolResolver.cs:89, ScriptExecutionSupervisor.cs:341-345.
- (d) Enforcement. Add a new `src/BannedSymbols.txt`, wired by a new `src/Directory.Build.props` that imports the root props via `GetPathOfFileAbove` and adds `AdditionalFiles`. Root `Directory.Build.props:32` wires the root BannedSymbols.txt into EVERY project, tests/analyzers/samples included, so the ban must not go in the root file.
  - Banned: all 5 `System.ArgumentException` constructors and all `System.ArgumentOutOfRangeException` constructors.
  - Allowed: the `PublicArgumentException` constructor, `ArgumentErrors.Redacted`, BCL `ThrowIf*` helpers, and `ArgumentNullException` constructors.
  - Only pragmas: a scoped `#pragma warning disable RS0030` in the two Core definitions, added by the guard child.
  - RS0030 + `TreatWarningsAsErrors` (`Directory.Build.props:6`) turns any violation into a build error.
  - Verify that BannedApiAnalyzers merges both BannedSymbols.txt AdditionalFiles; unverified.
- (e) `BuildSafeArgumentMessage` collapses to: sanctioned-root check, then `IPublicMessageException`, then the generic `Parameter '{ParamName ?? <unknown>}' is invalid`. Retire the arms at :590 parametersJson, :596 projectName, :601 lineRange, :608 startLine, :614 workspaceId candidates, :625 pattern sentinel, :634 catalog diff, :639 filePath+column. The marker check precedes them, so each arm is dead once its source site turns Public; `tool-error-handler-argument-sniffing-arms-retired` deletes them. The :585 arm goes in the prompt child (deleting `PromptParameterBindingException` forces it: CS0246).
- Wire compat (contract-care):
  - Category stays InvalidArgument for every argument site; messages only get more specific (additive).
  - Child 1 normalizes `exceptionType` to the BCL base name for marker exceptions, so ~220 conversions never flip that field. Only the existing Public* sites change from `Public*` back to BCL names, recorded as Changed.
- Publication policy (in `PublicArgumentException` remarks):
  - May echo: ints, bounds, valid-value lists, and server-derived identifiers.
  - Must not echo: absolute paths, raw caller free text (patterns, JSON, tokens, version strings), or lower-layer exception messages.

## Paused worktree (`.worktrees/argument-exception-throw-sites-lack-public-message`)

| Part | Fate |
|---|---|
| ParameterValidation.cs, SymbolTools.cs:43/:411 and the ParameterValidation/SymbolSearchPagination/FindReflectionUsagesPagination/ExpandedSurfaceIntegration test edits | Carry to `argument-exception-throw-sites-lack-public-message`; rebase onto child 1 |
| ScriptingTools.cs:33, UndoTools.cs:30/:81 and the ErrorResponseObservabilityTests edit | Carry to `tool-scripting-undo-workflow-argument-refusals-public-message`; also convert UndoTools.cs:85 |
| SymbolLocatorFactory.cs:69/:73 paramName-only | Changes to PublicArgumentException, in `symbol-locator-and-source-text-tool-argument-refusals-public-message` |
| ServerSurfaceCatalog.cs:298 paramName-only | Moves to `resource-argument-refusals-public-message`; rewrite without echoing versions, Public |
| src/RoslynMcp.Host.Stdio/BannedSymbols.txt + csproj AdditionalFiles | Dropped: superseded by the src-wide ban |
| changelog.d fragment | Re-draft per child |

## Survey (HEAD f7b33b85)

Host.Stdio 57 + 4 AOORE in 26 files; Roslyn 155 + 8 in 44 files; Core 1. BCL ThrowIf* 125 and `new ArgumentNullException(` 24 stay allowed.

## Bad code flagged (Directive 3)

- ParameterObjectService.cs: all 28 refusals reach callers as `Parameter 'request' is invalid`.
- ToolErrorHandler.cs:585-644: 9 sniffing arms.
- WorkspaceResources.cs:198 wraps raw `ex.Message`. It is dead text today (ResourceReadResultFilter.cs:90 unwraps the inner exception) but a latent leak.
- ClientRootPathValidator.cs:179-189 identifies its refusal by exact `GetType()` plus a `Data` marker, which is fragile.
- Internal invariants thrown as ArgumentException are misclassified as caller errors.

## Order

| Gen | Children |
|---|---|
| 1 | `public-argument-exception-core-move`, `argument-errors-redacted-factory`, and the 3 internal-guard children (no deps) |
| 2 | Public conversion children (dependsOn core-move + factory) |
| 3 | `tool-error-handler-argument-sniffing-arms-retired` |
| 4 | `argument-exception-public-message-guard` |

The guard does not need the arms child to compile. Its dependency on it is only there to satisfy the orchestrator's guard-last ordering; `set-depends-on` may drop it to co-schedule them.

