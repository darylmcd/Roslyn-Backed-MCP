# Changelog

All notable changes to Roslyn-Backed MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Fixed

- **`extract_method_preview` honours project formatting AND emits well-formed body + braces (`dr-9-7-produces-output-that-violates-project-formatting`, `dr-9-9-format-bug-004-produces-malformed-body-closing-b`).** Two audits — IT-Chat-Bot 2026-04-15 §9.7 and Firewall-Analyzer 2026-04-15 §9.9 (FORMAT-BUG-004) — reported the same root cause from opposite directions in `ExtractMethodService`: synthesized call sites read `var x=Method(a,b);` (no spaces around `=` or `,`); the new method declaration landed at column 1 with no class-scope indent; multi-line LINQ chains were collapsed onto one line; method-closing and class-closing braces glued together as `}}`; body statements lost indentation; and the file picked up a stray trailing `-}` in the diff. Single root cause for both shapes: `BuildMethodAndCallSite` synthesized the new method via `SyntaxFactory.MethodDeclaration(...).NormalizeWhitespace()` in isolation (no document-wide formatter pass), the call statement was raw `SyntaxFactory.AssignmentExpression`/`LocalDeclarationStatement` with no trivia, and manual `Whitespace("        ")` + `CarriageReturnLineFeed` hacks pinned indentation that didn't match the target file. Fix: drop `NormalizeWhitespace`, drop the manual whitespace hacks, tag both the synthesized method and the call statement with `Formatter.Annotation`, wrap the new method body in `ElasticCarriageReturnLineFeed` brace tokens, and route the resulting document through `Formatter.FormatAsync(document, Formatter.Annotation, …)` from `PreviewExtractMethodAsync` so the project's editorconfig drives spacing, brace placement, and class-scope indentation. The annotation scopes the formatter to just the new nodes and their immediate context — surrounding code stays byte-identical, preserving the dependency-inversion-noisy-diff invariant that the existing `ReplaceStatementsAndInsertMethod` was designed for. Regression test `ExtractMethodFormatRegressionTests` covers all three shapes (call-site spacing + declaration indent for §9.7, no `}}` glue + body indentation for §9.9, and a third test that asserts the output is byte-identical to a Roslyn `Formatter.Format` round-trip — i.e., the extraction is idempotent under the formatter, which is the strongest available "no formatting violation" oracle).

- **`migrate_package_preview` emits readable multi-line ItemGroup XML (`dr-9-4-format-bug-003-produces-inline-itemgroup-xml`).** Firewall-analyzer audit 2026-04-15 §9.4 (FORMAT-BUG-003) reproduced: migrating a package produced `<ItemGroup><PackageReference Include="…" /></ItemGroup></Project>` on a single line, left an empty prior `<ItemGroup>` behind, and in some csproj shapes wrote an `encoding="utf-16"` XML declaration to disk — fatal on the next `XDocument.Load` ("There is no Unicode byte order mark. Cannot switch to Unicode."). Root cause: `PackageMigrationOrchestrator` serialized via `XDocument.ToString(SaveOptions.DisableFormatting)`, which strips formatting entirely; the local `OrchestrationMsBuildXml.GetOrCreateItemGroup` helper then appended a raw `<ItemGroup>` without surrounding whitespace trivia; and removal used `XElement.Remove()` which left orphan whitespace text nodes. Fix: `OrchestrationMsBuildXml` gains `FormatProjectXml` (UTF-8 MemoryStream-backed `XmlWriter` with `Indent=true`, `IndentChars="  "`, detected line ending, `OmitXmlDeclaration` mirroring the source), `AddChildElementPreservingIndentation` (inserts children with sibling-matched indent), and `RemoveElementCleanly` (removes the element + adjacent whitespace + prunes empty parent ItemGroup). The existing `GetOrCreateItemGroup` now inserts a leading newline+indent AND, when the previous last element had no trailing whitespace, a trailing newline so `</Project>` lands on its own line. `PackageMigrationOrchestrator` uses all four helpers for both per-project csproj edits and the `Directory.Packages.props` central-version upsert. Regression tests `FORMAT_BUG_003_Migrate_Package_Preview_Emits_MultiLine_ItemGroup_With_Matching_Indent` (asserts no inline `<ItemGroup><PackageReference` in added diff lines, no glued `</ItemGroup></Project>`, each PackageReference at four-space indent) and `FORMAT_BUG_003_Migrate_Package_Preview_Preserves_Utf8_Declaration_When_Present` (asserts no `utf-16` declaration escapes to disk and `XDocument.Load` succeeds on the apply output) cover both halves.

- **`dependency_inversion_preview` preserves source-file formatting (`dr-9-3-format-bug-002-destroys-source-formatting`).** Firewall-analyzer audit 2026-04-15 §9.3 (FORMAT-BUG-002) reproduced: running `dependency_inversion_preview` against a source class re-flowed the entire source compilation unit — collapsed distinctive parameter-list spacing, dropped blank lines between members, reshuffled indentation, mixed line endings — even though only a single `: IName` base type was supposed to be added. Root cause: `CrossProjectRefactoringService.CreateInterfaceExtractionSolutionAsync` (the `dependency_inversion_preview`-only helper, distinct from `PreviewExtractInterfaceAsync`) called `((CompilationUnitSyntax)updatedSourceRoot).NormalizeWhitespace()` on the ENTIRE source after the targeted `ReplaceNode` edit. A secondary bug: the constructor-parameter replacement at line 250 built the new `TypeSyntax` via `SyntaxFactory.ParseTypeName(resolvedInterfaceName)` with no trivia, so the trailing space between type and parameter name was lost (`IAnimalServiceservice`). A tertiary bug in `AddBaseType`: when inserting a new base list after a class identifier whose trailing trivia was an `EndOfLineTrivia` (brace on its own line — the common case), the `\n` stayed attached to the identifier and the base list was orphaned on a new line (`class Foo\n : IName\n{`). Fix: dropped the `NormalizeWhitespace` call (mirrors `PreviewExtractInterfaceAsync`'s deliberate omission); parameter-type replacement uses `ParseTypeName(name).WithTriviaFrom(original.Type!)` to preserve the original trivia; `AddBaseType` relocates the identifier's trailing `EndOfLineTrivia` to the brace's leading trivia before attaching the new base list, so the output reads `class Foo : IName\n{`. Also closes `dr-9-10-format-bug-005-renders-declaration-without-spac` — transitively resolved by I-02's `ParseParameter` + `BuildParameterListFromTextChange` refactor (PR #166, `ChangeSignatureService.cs:101-103` already emits `"{ParameterType} {Name} = {DefaultValue}"` with correct spacing). Regression test `Dependency_Inversion_Preview_Preserves_Source_File_Formatting` seeds a source file with distinctive whitespace (multi-space parameter list, two blank lines between members) and asserts both source and consumer survive the round-trip intact.

- **`fix_all_preview` distinguishes empty-result reasons via `guidanceMessage` (`code-fix-provider-bundle`: `dr-9-2-no-code-fix-providers-loaded-for-any-exercised-d`, `dr-9-4-returns-for-some-ide-series-diagnostic-ids`, `dr-9-5-returns-silent-empty-result-for-info-severity-di`, `severity-flag-observable-tool-behaviour-is-inconsist`).** Three audits (SampleSolution §9.2, NetworkDocumentation §9.4, IT-Chat-Bot §9.5) plus a FLAG row reported the same observable defect: `fix_all_preview` returned `{ fixedCount: 0, changes: [], guidanceMessage: null }` for multiple distinct reasons that the caller could not distinguish — "no occurrences in scope", "no CodeFixProvider registered", "FixAll provider threw / produced no action / produced no ApplyChangesOperation" all looked identical on the wire. `FixAllService.PreviewFixAllAsync` now emits a scenario-specific `guidanceMessage` on every empty-result path. New helpers `BuildNoOccurrencesGuidance` (names the scope, filePath, or projectName and confirms that a provider IS registered) and `BuildProviderHasNoActionsGuidance` (explains the provider's Fixable-check rejection pattern and points at `add_pragma_suppression` / `set_diagnostic_severity` for non-fixable rules) pin the text shape for audit-report grepping. The existing "no provider registered" branch is extended with the same suppression / severity-bump hint so Info/IDE-series diagnostics without a built-in fix get an actionable next-step instead of just a restore-analyzer-packages suggestion. The `FixAllProvider.GetFixAsync` / `GetOperationsAsync` throw paths now include the exception type + message in the guidance so agents can debug without trawling server logs. New `FixAllServiceGuidanceTests` (8 cases) covers all three scenarios plus the happy-path invariant that a non-empty preview token must carry a positive `fixedCount`.

- **Cross-project interface extraction preserves generated-file whitespace (`dr-9-2-format-bug-001-cross-project-interface-extractio`).** Firewall-analyzer audit 2026-04-15 §9.2 (FORMAT-BUG-001) reproduced: `extract_interface_cross_project_preview` (and `extract_and_wire_interface_preview` for the interface half of `dependency_inversion_preview`) emitted an interface file whose content read `publicinterfaceICollectionServiceProbe{Task<string>RunAsync(ScopeFilter?scopeOverride,…);}` — every keyword, identifier, brace, parameter-type and parameter-name fused together — and in the same edit glued the consumer class's `{` onto the newly-inserted `: IName` base type. Root cause: `CrossProjectRefactoringService.CreateCompilationUnitForMember` built the new compilation unit from raw `SyntaxFactory` nodes (interface declaration, members, file-scoped namespace) that carry no trivia, then emitted `ToFullString()` without running the formatter; `AddBaseType` built a raw `SimpleBaseType` without leading whitespace and never adjusted the type body's `OpenBraceToken` trivia. Fix: `CreateCompilationUnitForMember` gains a `normalizeWhitespace` flag — the synthesized-interface path (`CreateInterfaceCompilationUnit`) passes `true` so the entire new compilation unit goes through `NormalizeWhitespace()` (safe: the file is brand-new, there is no original formatting to preserve) while the move-type path keeps its trivia-preserving build shape. `AddBaseType` now attaches a leading space to the synthesized `SimpleBaseType`, uses Roslyn's `BaseListSyntax.AddTypes` to manage comma separators on existing lists, and routes the resulting declaration through a new `EnsureOpeningBraceOnOwnLine` helper (mirroring `InterfaceExtractionService`) so the class body's `{` lands on its own line instead of being glued to the interface name. Regression test `Extract_Interface_Preview_Generates_Formatted_Interface_File_Across_Projects` asserts both the interface file and the source file emerge with readable whitespace and the class body remains multi-line.

- **`extract_type_preview` preserves the blank line between namespace declaration and class (`dr-9-5-strips-the-blank-line-between-namespace-and-clas`).** SampleSolution audit 2026-04-15 §9.5 reproduced: extracting a member produced a generated file where `namespace Foo;` ran directly into `public sealed class NewType` with no blank line between them, violating standard C# layout (and the `dotnet format` / editorconfig default). Root cause: `TypeExtractionService.BuildNewFileRoot` calls `CompilationUnitSyntax.NormalizeWhitespace()` on the freshly-synthesized compilation unit, which emits only a single newline between a namespace declaration and its first type member. Fix: a new `EnsureBlankLineBetweenNamespaceAndType` helper runs after `NormalizeWhitespace()` and prepends an `EndOfLineTrivia` to the first type declaration inside either a file-scoped or block namespace — idempotent (skips if a leading `EndOfLineTrivia` already sits there), trivia-surgical (the rest of the normalized output is untouched), and scoped to the new-file path so apply-side source-file edits keep their existing formatting contract. Regression test `ExtractType_PreservesBlankLineBetweenNamespaceAndClass` creates a fresh fixture in the copied SampleLib, runs `PreviewExtractTypeAsync`, and asserts the line immediately following the namespace declaration is empty while the class declaration follows on the next line. Verified to fail pre-fix and pass post-fix.

- **`extract_type_apply` strips the `override` modifier when the new type does not inherit the base (`dr-9-3-preserves-when-new-type-does-not-inherit-the-bas`).** IT-Chat-Bot audit 2026-04-15 §9.3 reproduced: extracting `Down()` from a class inheriting `Migration` produced `public override void Down(...)` inside the new `public sealed class` and yielded CS0115 on the first `compile_check` ("no suitable method found to override"). The new type is emitted via `SyntaxFactory.ClassDeclaration(...)` with NO base list, so inheritance-only modifiers (`override`, `virtual`, `abstract`, member-level `sealed`, `new`) either fail to compile (CS0115, CS0549) or silently hide nothing. New `TypeExtractionService.StripInheritanceOnlyModifiers` composes with `EnsurePublicAccessibility` inside `BuildNewFileRoot` to remove those modifiers from every extracted member while preserving the leading trivia of the original first modifier so the declaration keeps its line break. Defensive guard throws an actionable `InvalidOperationException` if a future caller tries to extract an abstract member that has no body. Regression test `ExtractType_OverrideMember_StripsOverrideFromNewType` constructs a `BaseMigration` + derived fixture with `override` and `sealed override` members and asserts no forbidden modifier appears on the added diff lines.

- **`bulk_replace_type scope=parameters` walks generic arguments in implemented interfaces (`dr-9-6-ignores-generic-arguments-in-implemented-interfa`).** IT-Chat-Bot audit 2026-04-15 §9.6 reproduced: when a class implements a generic interface parameterised by the old type (`class Foo : IValidateOptions<OldType> { bool Validate(OldType options) … }`), `bulk_replace_type scope=parameters` rewrote the method parameter but skipped the interface base-list generic argument, leaving the class with a signature that violated the interface's exact-match rule and a broken workspace. `BulkRefactoringService.ShouldReplace` now tracks whether the parent walk crossed a `GenericNameSyntax` / `TypeArgumentListSyntax` boundary; when it did, `scope=parameters` additionally treats a `SimpleBaseTypeSyntax` parent as a valid replacement site so the implemented-interface contract stays in sync with the rewritten parameter types. Direct base-list references without a generic boundary remain `scope=all`-only. Tool description, `IBulkRefactoringService` XML docs, and the `ServerSurfaceCatalog` summary are updated to describe the new semantics.

### Changed

- **Bootstrap caveat for `*_apply` is now scoped to main-checkout self-edit only (docs-only, no backlog row).** The previous framing in `ai_docs/runtime.md`, `ai_docs/bootstrap-read-tool-primer.md`, `ai_docs/prompts/backlog-sweep-execute.md`, `.github/copilot-instructions.md`, and `.cursor/rules/operational-essentials.md` treated every session on this repo as bootstrap-restricted, forbidding `*_apply` Roslyn MCP tools across the board. The underlying rationale — "the binary servicing the MCP call is the binary being edited" — only holds when the running MCP server IS the checkout under edit (e.g. `dotnet run --project src/RoslynMcp.Host.Stdio` against that same checkout). Worktree-based subagent sessions under `ai_docs/workflow.md` run against the installed global `roslynmcp` tool — a distinct, already-built binary — so edits in `.worktrees/<id>/` cannot mutate the binary servicing their calls. The runtime doc now splits the bootstrap section into two sub-cases (main-checkout vs worktree), the primer's write-side table records the worktree carve-out, and the backlog-sweep executor prompt's Step 5 explicitly permits `*_apply` in worktrees. Behavioural impact: future backlog-sweep subagents may use Roslyn MCP refactor tools (`rename_apply`, `extract_type_apply`, etc.) directly instead of falling back to `Edit`/`Write` when a tool covers the operation — load the worktree's own `RoslynMcp.slnx`, `workspace_reload` after apply if a downstream call needs a refreshed snapshot.

- **Single SDK-filter error boundary for every `tools/call` (closes `pre-binding-failures-emit-bare-error-string`).** The SDK's reflection-based argument binder throws `ArgumentException` / `JsonException` / `FormatException` BEFORE the tool method runs when a required parameter is missing, an arguments key is unknown, or the JSON is malformed. The legacy per-handler `ToolErrorHandler.ExecuteAsync(...)` wrapper could not observe these — it only saw exceptions raised inside its lambda — so the SDK surfaced a bare `"An error occurred invoking '<tool>'."` string with no category, no tool name, and no parameter name, forcing callers to fetch the schema via `ToolSearch` before they could self-correct. Fix: install a single `StructuredCallToolFilter` via `WithRequestFilters(b => b.AddCallToolFilter(StructuredCallToolFilter.Create))` in `Program.cs`. The filter wraps the SDK dispatcher itself, so it observes BOTH pre-binding and post-binding exceptions (SDK PR [csharp-sdk#844](https://github.com/modelcontextprotocol/csharp-sdk/pull/844), shipped in 0.4.0-preview.3 and carried into the 1.1.0 pin) and returns a structured `CallToolResult { IsError = true, Content = [TextContentBlock { Text = envelope }] }` for every failure mode. The envelope still carries `error: true`, `category`, `tool`, `message`, `exceptionType` with `_meta` gate-timings injected, so the LLM gets the exact diagnostic it needs to retry (MCP SEP-1303 governance on input-validation errors as tool-execution errors, not JSON-RPC protocol errors). `ToolErrorHandler.ClassifyError` extended with `TryClassifyBindingLike` so it handles binding-family exceptions (`ArgumentNullException`, `ArgumentOutOfRangeException`, `ArgumentException`, `JsonException`, `FormatException`) in both wrapped and raw shapes — previously the wrapped path was the only one and raw exceptions fell through to generic dictionary handling that dropped the `ParamName`. The per-handler `ToolErrorHandler.ExecuteAsync` wrapper was then swept from all 50 tool files (174 call sites) and deleted from production; unit tests that exercised the classifier through the legacy wrapper now go through a test-project-only `ToolExecutionTestHarness.RunAsync` that mirrors the filter's control flow. New `StructuredCallToolFilterTests` covers pre-binding missing-required-parameter, `ArgumentNullException` with `ParamName`, JSON deserialization failure, handler `KeyNotFoundException` → `NotFound`, unrecognized handler exception → `InternalError` with abbreviated stack trace, and the `_meta` injection success-path including the bare-array pass-through contract that preserves `source_generated_documents`-shaped responses. New reference doc [`ai_docs/references/mcp-server-best-practices.md`](ai_docs/references/mcp-server-best-practices.md) ties this decision to spec / SEP / SDK / community evidence and is wired into the `ai_docs/README.md` index under "Change error handling, tool-call dispatch, filters, or `Program.cs`" so future design reviews pick up the same framing.

- **Bootstrap read-tool guidance is now explicit and canonical (closes `bootstrap-read-only-roslyn-mcp-checklist-for-self-edit-sessions`).** Four consecutive self-development sessions (v1.15.0 / v1.16.0 / PRs #165–#178 / PRs #182–#194) reproduced the same anti-pattern: agents generalized the `bootstrapCaveat` — which restricts **write-side** `*_apply` only — to the entire Roslyn MCP surface, falling back to `Grep` / `Bash: dotnet build` / `Bash: dotnet test` when `find_references` / `compile_check` / `test_run` were explicitly permitted and 5–30× faster. Root-cause fix (the prompts themselves taught the anti-pattern, so the prompts themselves change):
  - New canonical cheat-sheet `ai_docs/bootstrap-read-tool-primer.md` with the session-verb → tool mapping, pattern anti-patterns, and fallback-column for disconnected-server scenarios.
  - Promoted to item #5 in `AGENTS.md` / `CLAUDE.md` bootstrap read order (was: runtime.md → backlog.md; now: runtime.md → primer → backlog.md).
  - `ai_docs/runtime.md` § *Roslyn MCP client policy (AI sessions)* rewritten into three parts: **read-side** preference for every session including bootstrap (was missing entirely); **write-side** preview→apply for peer repos; **bootstrap scope** explicitly restricting only `*_apply` on this repo while read-side and `*_preview` remain fully supported.
  - `ai_docs/prompts/backlog-sweep-execute.md` Step 5 replaced the literal `Bash: dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` instruction with a preference-order that puts `mcp__roslyn__compile_check` first and `dotnet build` as the disconnected-server fallback; added equivalent preference-orders for `test_run` vs `dotnet test`, `find_references` vs `Grep`, `symbol_search` vs `Grep`, `document_symbols` vs `Grep public `.
  - `ai_docs/prompts/backlog-sweep-plan.md` bootstrap-caveat paragraph scoped to write-side (was conflating write + read); links the primer.
  - `.github/copilot-instructions.md` + `.cursor/rules/operational-essentials.md` gain the same read-side rule as their top bullet under "Roslyn MCP".

  Zero code changes — all changes are in docs / prompts / bootstrap lists. Closes the backlog row that tracked this pattern.

## [1.20.0] - 2026-04-16

Backlog sweep 2026-04-16 continuation — 14 additional initiatives shipped (PRs #182–#197), closing 14 backlog rows (all P4). Race-aware error envelopes, pagination everywhere (test_reference_map, catalog), structured JSON error envelopes for resources and prompts, strict symbol resolution for `symbol_info`, and cross-interface callsite summaries dominate. Three BREAKING response-shape changes (`symbol_info` strict default, `restructure_preview` placeholder validation, `roslyn://server/catalog` summary).

### Fixed

- **Reader tools surface a race-aware `WorkspaceReloadedDuringCall` error when the gate auto-reloaded mid-call (`symbol-impact-sweep-race-with-auto-reload`).** Jellyfin audit 2026-04-16 §9 reported `symbol_impact_sweep` returning "No symbol could be resolved" during concurrent `workspace_reload`, even though the symbol did still exist — callers reading the response treated it as a real miss and gave up. Root cause: the gate auto-reloaded under `ROSLYNMCP_ON_STALE=auto-reload` and `SymbolResolver.ResolveOrThrowAsync` then saw a `SymbolHandle` encoded against the pre-reload compilation that no longer round-tripped, throwing `KeyNotFoundException` with the generic "handle may be from a previous workspace version" message. `ToolErrorHandler.ClassifyError` now inspects the ambient gate metrics: when the current request's `StaleAction == "auto-reloaded"` and the exception is a `KeyNotFoundException`, it emits a distinct `WorkspaceReloadedDuringCall` category with retry guidance (re-resolve the symbol via `symbol_search` / `symbol_info` or a fresh position locator) instead of the generic `NotFound`. Solution-wide — every read tool that throws `KeyNotFoundException` under the same race picks up the new envelope; unrelated `NotFound` paths are unchanged. `_meta.staleReloadMs` already exposes how long the reload held the request, so callers can size their backoff.
- **`source_file_lines` resource returns structured JSON error envelopes for invalid inputs (`dr-9-13-flag-resource-invalid-range-resource-returns-ge`).** Firewall-analyzer audit 2026-04-15 §9.13 reported that calling the resource with `endLine < startLine`, non-numeric bounds, or a `startLine` past EOF produced a generic JSON-RPC `-32603` with no machine-readable category — callers could not distinguish "bad range" from "backend crash". `GetSourceFileLines` now executes inside `ToolErrorHandler.ExecuteResourceAsync`, which maps the validation-failure exceptions (`ArgumentException`, `ArgumentOutOfRangeException`, `KeyNotFoundException`) to the standard `{error, category, tool, message, exceptionType?}` envelope. Success responses are unchanged (still the marker-prefixed C# slice at `text/x-csharp`); only the error path picks up the structured shape with the resource URI template as the `tool` field. The three `ParseLineRange` branches that previously surfaced `InvalidOperationException` now throw `ArgumentException`/`ArgumentOutOfRangeException` so they classify as `InvalidArgument` rather than `InvalidOperation`.
- **`validate_workspace` surfaces fabricated / unknown `changedFilePaths` as `unknownFilePaths` (`dr-9-8-bug-validate-fabricated-accepts-fabricated-silen`).** Firewall-analyzer audit 2026-04-15 §9.8 reported that callers could pass paths that didn't exist in the workspace (typo, stale audit-report reference) and `validate_workspace` silently dropped them inside `TestDiscoveryService.FindRelatedTestsForFilesAsync` — the response claimed success over an empty scope with no hint that part of the caller's list had been ignored. `WorkspaceValidationService.ResolveChangedFiles` now partitions the caller-supplied list into known and unknown based on the workspace's document set and surfaces both on the response DTO (`changedFilePaths` + new `unknownFilePaths`). The change-tracker fallback path is unchanged — tracker entries originate from Roslyn document mutations inside this process so every path is known by construction.
- **`restructure_preview` refuses goal patterns that reference uncaptured `__name__` placeholders (`dr-9-1-regression-r17a-emits-literal-placeholder`).** Firewall-analyzer audit 2026-04-15 §9.1 (REGRESSION-R17A) reproduced goal output containing the literal string `__name__`. Two orthogonal bugs: (1) `Substitute` short-circuited on `_placeholderNames.Count == 0` where the count came from the PATTERN — any pattern without placeholders returned the goal unmodified, literal placeholders and all. (2) Goal-only placeholders (present in goal, absent in pattern) silently passed through the `PlaceholderSubstituter` walker because no capture ever filled them. Fix: extract placeholder names from BOTH pattern AND goal; reject up-front with an actionable `ArgumentException` naming every orphaned placeholder (`goal references placeholder(s) not captured by the pattern: __count__`); gate the `Substitute` fast path on the GOAL's placeholder count instead of the pattern's. Literal-only pattern/goal pairs remain supported. New `RestructureServiceTests` covers both mismatch shapes plus the literal-preserving case.
- **`project_graph` always populates `name` and `filePath` on every project node (`project-graph-missing-metadata-fields`).** Downstream project-lookup flows (`scaffold_test_preview`, DI resolution) rely on a non-empty `name` — pre-fix, projects whose underlying `Microsoft.CodeAnalysis.Project.Name` surfaced as an empty string (MSBuild evaluation race, unusual csproj shape) emitted `name: ""` into the graph and broke the lookup. New `ProjectMetadataParser.ResolveProjectName(project)` falls back through (1) the populated AssemblyName, (2) a filename-derived stem, (3) the literal `"unknown"` sentinel. Sibling `ResolveProjectPath(project)` coalesces an empty FilePath to `"unknown"`. `WorkspaceManager.BuildProjectStatuses` now routes both top-level project entries and inter-project references through the resolvers. Regression test asserts every graph node carries non-empty `name` + `filePath`.
- **Staleness policy `AutoReload` only stamps `staleAction: "auto-reloaded"` when the reload actually ran (`dr-9-9-response-claims`).** Firewall-analyzer audit 2026-04-15 §9.9 reported response envelopes advertising `staleAction: "auto-reloaded"` in the `_meta` block while no reload had actually happened. Root cause: `WorkspaceExecutionGate.ApplyStalenessPolicyAsync`'s `finally` block stamped `AmbientGateMetrics.Current.StaleAction = "auto-reloaded"` unconditionally — even when `ReloadAsync` short-circuited on `KeyNotFoundException` (workspace closed between the stale check and the reload attempt). Fix: track whether the reload succeeded with a local `reloaded` flag; stamp `StaleAction` + `StaleReloadMs` only when the reload body completed without throwing. `workspace_close` invocations against a stale workspace whose registry entry vanished mid-call no longer carry the false auto-reload claim.
- **`get_prompt_text` returns structured `InvalidArgument` envelope for malformed `parametersJson` (`dr-9-7-bug-json-parse-surfaces-stack-trace`).** Firewall-analyzer audit 2026-04-15 §9.7 reported that a syntactically invalid `parametersJson` (e.g. `"{not valid"`) or a property whose JSON value did not match the prompt parameter's declared type leaked a `System.Text.Json.JsonException` stack trace to the MCP client as `InternalError`. Root cause: the `JsonException` was not the immediate `InnerException` of an invocation wrapper, so `ToolErrorHandler.ClassifyError`'s parameter-binding detector did not match and the error fell through to the internal-error fallback. `PromptShimTools.BuildParameterValuesAsync` now wraps both the top-level `JsonDocument.Parse` and each per-parameter `JsonSerializer.Deserialize` in explicit try/catch blocks and re-throws as `ArgumentException` with `nameof(parametersJson)` as `ParamName`. That maps via the exact-type handler to `InvalidArgument` and the message now names the offending parameter (e.g. `"parametersJson property 'category' could not be deserialized into String: ..."`) instead of a generic stack trace.

### Added

- **`test_related` finds tests through interface dispatch via reference-sweep augmentation (`test-related-empty-for-valid-symbol`).** Firewall-analyzer audit 2026-04-13 §14: `test_related` returned `[]` for valid symbols with visible test coverage when the tests dispatched through an interface (tests call `IService.Method()` but the symbol being queried is the concrete `MyService.Method`, or the interface member where no test name contains the interface name). Pre-fix the service matched only on symbol-name substring against test method/class/file names. Fix: after the heuristic pass, `TestDiscoveryService.FindRelatedTestsAsync` walks the symbol + every implementation / override via `SymbolFinder.FindImplementationsAsync` + `FindOverridesAsync`, then sweeps `SymbolFinder.FindReferencesAsync` for each and collects the unique test file paths. Any discovered test whose `FilePath` matches a referenced file is merged into the result (deduped by `FullyQualifiedName`). Heuristic pass still runs first so simple-name overlap remains a zero-cost fast path; the sweep augments rather than replaces it. Reference sweep is best-effort — cancellation propagates but any other failure falls back to the heuristic-only shape with a warning log. Tool description updated to describe the two-pass contract.
- **`test_coverage` short-circuits with a structured `CoverletMissing` envelope when `coverlet.collector` isn't referenced (`test-coverage-vague-error-when-coverlet-missing`).** IT-Chat-Bot audit 2026-04-13 §18: the tool silently "succeeded" — `success=true`, tests actually ran, but no coverage file emerged — with only a vague "Coverage file not generated" string in the body. Callers couldn't distinguish "package missing" from "runtime failure". Fix: inspect the target test project(s) BEFORE launching `dotnet test`; if any candidate csproj doesn't reference `coverlet.collector`, return immediately with `success=false`, `failureEnvelope.errorKind="CoverletMissing"`, and a new `missingPackages: string[]` listing the offending test project names. Callers now get a machine-readable path from "error" to "`dotnet add package coverlet.collector`". The post-run fallback at the same `ErrorKind` is preserved for the rare case where coverlet IS referenced but the coverage XML doesn't materialize.
- **`symbol_search` matches against fully-qualified names (`symbol-search-partial-match-gap`).** IT-Chat-Bot audit 2026-04-13 §9.5: searching `"ITChatBot.Conversation.ConversationManager"` returned `[]` because Roslyn's `FindSourceDeclarationsWithPatternAsync` matches only the simple name, not namespace-qualified queries. `SymbolSearchService.SearchSymbolsAsync` now runs a second FQN-substring pass after the primary pattern search — enumerates every named type (including nested) in every project and adds results whose `ToDisplayString(QualifiedNameOnlyFormat)` contains the query case-insensitively. Member-level symbols (methods, properties, fields) are also swept so `"SampleLib.AnimalService.CountAnimals"` finds the method. Primary-pass duplicates are de-duped via `ToDisplayString()` key. Only runs while under the caller-supplied `limit`, so cheap queries pay no cost beyond the primary pattern search.
- **`test_reference_map` gains pagination + honours `projectName` for productive-project scoping (`dr-9-5-bug-pagination-001-has-no-pagination-filter-igno`).** SampleSolution audit 2026-04-15 §9.5 reported two bugs: (1) the tool returned the full covered/uncovered symbol set in one response — blows the MCP cap on medium-sized solutions; (2) the `projectName` parameter only scoped which TEST projects were scanned, so passing a productive project name ("is MY project tested?") left the productive set unfiltered. Fix: two new parameters `offset: int = 0` and `limit: int = 200` page through the combined covered-first/uncovered-next stream (limit clamped to [1, 500]); response DTO gains `offset`, `limit`, `totalCoveredCount`, `totalUncoveredCount`, `hasMore` so callers can resume without re-computing. `projectName` now also filters the productive-symbol collection when the name matches a productive project (previously only influenced the test scan). `CoveragePercent` stays pegged to the full totals so the verdict is stable across page sizes. New `TestReferenceMapServiceTests` covers all 6 shapes (default page, limit=1, offset past end, coverage stability, productive-project scoping, unknown-name error).
- **`change_signature_preview op=remove` accepts caret on the parameter itself (`change-signature-parameter-span-hint-for-remove`).** Firewall-analyzer 2026-04-15 reported UX friction: the tool required the caret on the method declaration plus an explicit `position` (0-based, error-prone) or `name` (transcribed verbatim). Agents with a caret on the parameter got `requires a method symbol` and had to re-click or translate the name into a position. When `SymbolResolver` returns an `IParameterSymbol`, the service now auto-promotes to the containing method and splices the parameter's index into `request.Position` — as long as the caller didn't already supply `Position` or `Name` (explicit values still win). `add` and `rename` benefit from the same promotion for consistency; `remove` is the primary UX win. Two new regression tests cover auto-resolve and explicit-override precedence.
- **`roslyn://server/catalog` paginated (`dr-9-11-payload-exceeds-mcp-tool-result-cap`).** SampleSolution audit 2026-04-15 §9.11 reported the catalog exceeded the MCP tool-result cap (~80 KB JSON with 168 tools on a full surface). The default resource now returns a cap-safe summary — tool/prompt counts + `toolsResourceTemplate` / `promptsResourceTemplate` pointers + the (small) Resources list + workflow hints + metadata. Two new paginated siblings serve the full entries: `roslyn://server/catalog/tools/{offset}/{limit}` and `roslyn://server/catalog/prompts/{offset}/{limit}` (offset 0-based; limit clamped to [1, 200]; response carries `offset`, `limit`, `returnedCount`, `totalCount`, `hasMore`). For clients that can still absorb the full payload a new `roslyn://server/catalog/full` resource returns the pre-v1.19.1 shape unchanged. New `ServerCatalogSummaryDto` + `ServerCatalogPagedEntriesDto` records model the new responses. Parity test gains 4 new assertions (summary shape, offset/limit clamping, pagination metadata).

### Changed — BREAKING

- **`symbol_info` default resolution is now strict — caret on whitespace adjacent to an identifier returns NotFound (`symbol-info-lenient-whitespace-resolution`).** Jellyfin stress test 2026-04-15: calling `symbol_info` at `(line 31, col 1)` — a blank line below `ILibraryManager` — silently returned `ILibraryManager` via Roslyn's preceding-token fallback. Default behavior flipped: new `allowAdjacent: bool = false` parameter on `symbol_info`; when false (the default), `SymbolResolver.ResolveAtPositionAsync` rejects leading-trivia positions without walking to the previous token. Callers that want the pre-v1.19.1 shape pass `allowAdjacent=true` explicitly. The change is scoped to `symbol_info`'s `SymbolSearchService.GetSymbolInfoAsync` entry point — every other tool that calls `SymbolResolver.ResolveAsync` continues to use the lenient default because their UX relies on the preceding-token fallback (e.g., caret on '(' after a method name). New regression asserts strict-mode rejects leading whitespace while still hitting the identifier directly.
- **`restructure_preview` now rejects pattern/goal pairs with mismatched placeholder sets (`dr-9-1-regression-r17a-emits-literal-placeholder`).** Pre-fix, goal-only placeholders silently emitted literal `__name__` text in the output; callers now receive an `ArgumentException` at preview time. Tool schema unchanged; the error surface is the only breaking delta. Legitimate literal-only pattern/goal pairs (no placeholders on either side) continue to work.
- **`roslyn://server/catalog` response shape is now a summary (`dr-9-11-payload-exceeds-mcp-tool-result-cap`).** The `tools` and `prompts` arrays are replaced with `toolCount` + `toolsResourceTemplate` and `promptCount` + `promptsResourceTemplate`. Clients that relied on the inline arrays must either paginate via the new siblings or fetch `roslyn://server/catalog/full` for the pre-v1.19.1 shape.

### Maintenance

- **Backlog:** 14 rows closed under the 2026-04-16 sweep: `dr-9-13-flag-resource-invalid-range-resource-returns-ge`, `dr-9-7-bug-json-parse-surfaces-stack-trace`, `dr-9-9-response-claims`, `project-graph-missing-metadata-fields`, `dr-9-1-regression-r17a-emits-literal-placeholder`, `dr-9-8-bug-validate-fabricated-accepts-fabricated-silen`, `dr-9-11-payload-exceeds-mcp-tool-result-cap`, `change-signature-parameter-span-hint-for-remove`, `dr-9-5-bug-pagination-001-has-no-pagination-filter-igno`, `symbol-info-lenient-whitespace-resolution`, `symbol-search-partial-match-gap`, `test-coverage-vague-error-when-coverlet-missing`, `test-related-empty-for-valid-symbol`, `symbol-impact-sweep-race-with-auto-reload` (all P4).
- **Tests:** +34 regression tests — 3 in `Top10V2RegressionTests`, 3 in `PromptSmokeTests`, 1 in `WorkspaceExecutionGateTests`, 1 in `WorkspaceToolsIntegrationTests`, 3 in a new `RestructureServiceTests`, 3 in `ValidateWorkspaceSummaryTests`, 4 in `SurfaceCatalogTests`, 2 in `ChangeSignaturePreviewTests`, 6 in a new `TestReferenceMapServiceTests`, 1 in `SymbolResolverRenameCaretTests`, 2 in `IntegrationTests`, 1 in `ExpandedSurfaceIntegrationTests`, 1 in `ValidationToolsIntegrationTests` (interface-dispatch test_related), and 3 in a new `ToolErrorHandlerWorkspaceReloadRaceTests`. The two pre-existing `*_Throws` source-file-lines tests were rewritten as `*_ReturnsStructuredError` to reflect the new contract.

## [1.19.0] - 2026-04-16

Top-10 remediation pass v6 — ten correctness and safety fixes targeting the classes of bugs flagged across the 2026-04-15 experimental-promotion audits (firewall-analyzer, IT-Chat-Bot, NetworkDocumentation, SampleSolution) and the v1.18.2 Jellyfin stress test. Closes **19 backlog rows** (7 P2 · 6 P3 · 6 P4). Shipped as PR #162 — one commit per item, plus a baseline plan commit and a final backlog-sync commit.

Backlog sweep 2026-04-16 (plan: `ai_docs/plans/20260416T054040Z_backlog-sweep/plan.md`) — 14 additional initiatives shipped one PR each (PRs #165–#178), closing 17 more backlog rows (11 P3 · 6 P4). Summary-mode additions across 4 high-fan-out tools, tighter readiness contract, structured parameter-binding errors, and Windows-path resource normalization dominate the feature surface.

### Fixed

- **`roslyn://workspace/{id}/file/{filePath}` resource normalizes every Windows path encoding shape (`file-resource-uri-windows-path-handling`).** IT-Chat-Bot 2026-04-13 §14 reported absolute Windows paths with `:` and `\\` broke the resource URI. Centralized normalization into a new `NormalizeFilePathForResource` helper used by both `GetSourceFile` and `GetSourceFileLines`. Accepted shapes: fully URL-encoded (`C%3A%5CUsers%5Cfoo`), raw absolute (`C:\Users\foo`), forward-slash variant (`C:/Users/foo`), partially encoded (only `:` and `\\` escaped), and encoded forward-slashes (`%2F` sequences). Tool description now explicitly calls out that URL-encoded form is preferred for cross-client portability (some MCP clients reject raw Windows paths per RFC 3986 before they reach the server) but raw paths are accepted server-side. Regression suite exercises every encoding shape.
- **stdout flushed on stdin-EOF / process exit (`mcp-stdio-console-flush-on-exit`).** IT-Chat-Bot 2026-04-13 §9.4 reproduced clients receiving 0 bytes when stdin closed under a bash-pipe integration. Pre-fix the host flushed in `ApplicationStopping` + after `RunAsync` returns, but on stdin-EOF the SDK transport could exit fast enough that buffered MCP JSON responses were lost before the async `FlushAsync` completed. Added an `AppDomain.CurrentDomain.ProcessExit` handler that flushes synchronously — fires on every exit path including stdin-EOF where graceful shutdown may not run. Post-`RunAsync` block now also calls the synchronous `Console.Out.Flush()` before the existing `FlushAsync`. Three flush paths now: ProcessExit (sync, every exit), ApplicationStopping (sync, graceful shutdown), post-RunAsync (sync + async, belt-and-suspenders). Structural test guards against any future refactor that drops one of them.
- **`server_info.update.latest` no longer surfaces older-than-current registry values (`server-info-update-latest-inverted`).** Jellyfin 2026-04-16 §1 reproduced `latest=1.16.0` while `current=1.18.2` — the field was emitting any cached registry value regardless of comparison to current. New contract: `latest` is `null` unless the registry version is strictly greater than current; `updateAvailable` boolean is unchanged. New `ILatestVersionProvider` interface introduced so the inverted case is covered by a unit test (`ServerInfo_RegistryReportsOlderVersion_LatestIsNull_UpdateAvailableFalse`) without mocking HTTP. `NuGetVersionChecker` now implements the interface; production wiring unchanged.
- **Tool parameter-binding errors return structured `InvalidArgument` payloads with the offending parameter name (`mcp-parameter-validation-error-messages`).** Missing/invalid parameters were surfacing as generic "An error occurred invoking '&lt;tool&gt;'" with no indication of which parameter was wrong (NetworkDocumentation + IT-Chat-Bot 2026-04-13 — biggest single-fix debug-friction across the audits). `ToolErrorHandler.ClassifyError` now checks the immediate inner exception when the outer is a recognized invocation wrapper (`TargetInvocationException` or an `InvalidOperationException` whose message contains "invocation"). Five inner-shape cases produce structured `InvalidArgument` envelopes that name the parameter via `ArgumentException.ParamName`: `JsonException` ("Parameter binding failed (JSON deserialization)"), `ArgumentNullException` ("Required parameter 'X' is missing"), `ArgumentOutOfRangeException` ("Parameter 'X' has an out-of-range value"), generic `ArgumentException` ("Parameter 'X' is invalid"), and `FormatException` ("Parameter format error"). Scope is intentionally narrow — only the immediate inner is checked, and the outer must be a known wrapper — so legitimate domain errors that happen to have an Argument-like exception buried in their chain are NOT misclassified. Existing `BacklogFixTests` regression coverage preserved.
- **`extract_type_preview` refuses when extracted members are referenced by external consumer files (`dr-9-1-does-not-update-external-consumer-call-sites`).** SampleSolution audit §9.1 reproduced this as `extract_type_apply` silently breaking `SampleApp/Program.cs` after a public member was extracted. Pre-fix the preview was plausible but the apply moved the methods to a constructor-injected private field — every external `source.ExtractedMember()` call no longer compiled, and the agent had no way to see the breakage before applying. Plan originally proposed automatically rewriting external consumers, but that requires understanding their construction parameters / DI setup and would silently introduce subtle bugs. New `CollectExternalConsumerWarningsAsync` runs solution-wide `SymbolFinder.FindReferencesAsync` for each public/internal member to be extracted; any reference outside the source file becomes a refusal warning that names the affected file + project. Existing `ExtractType_FromAnimalService_CreatesPreview` test was the exact bug case (CountAnimals referenced from Program.cs + AnimalServiceTests) and is renamed to assert the new refusal; companion test `ExtractType_NoExternalConsumers_ProducesPreview` exercises the happy path on a fresh fixture with only internal references.
- **`change_signature_preview` enumerates all callsite files across interface implementations + overrides (`change-signature-preview-callsite-summary`).** `op=remove` on `IPanosClient.GetRegisteredIpsAsync` (firewall-analyzer 2026-04-15) returned a preview with only the interface-owner file; apply rewrote 4 callsite files (concrete impl + tests) invisibly, so the agent abandoned the preview and switched to manual `Edit` calls. New `CollectRelatedSymbolsAsync` walks the related-symbol set: for an interface method it includes every implementation; for an implementation it walks back to interface members + sibling implementations; virtual/abstract methods include all overrides + the base. `ApplyAddRemoveAsync` now (a) updates declarations on every related symbol (so an interface-method signature change keeps every implementer compiling) and (b) collects callers from every related symbol, deduplicating physical callsites that appear under multiple symbols. The caller-rewrite loop processes spans per-document in reverse order so each `WithDocumentText` doesn't invalidate the offsets of earlier callsites in the same file. New `CallsiteUpdates: IReadOnlyList<CallsiteUpdateDto>?` field on `RefactoringPreviewDto` exposes a compact `{filePath, callsiteCount}[]` summary so callers can audit total reach without parsing every diff. Caller resolution searches against the original solution (the post-declaration-rewrite accumulator's compilation is invalidated); document IDs are stable so the lookup then maps cleanly into accumulator's current text.
- **`change_signature_preview op=add` no longer concatenates the method body brace onto the signature line (`change-signature-preview-brace-concat-on-add-op`).** `PreviewAddParameterAsync` built the new parameter via raw `SyntaxFactory.Parameter` + a trailing-space hack on `ParseTypeName`; the resulting `ParameterListSyntax` (constructed via `SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters))`) carried no enclosing trivia, so `ReplaceNode` lost the close-paren's trailing trivia (the whitespace before the method body's opening brace). On Jellyfin `MusicManager.GetInstantMixFromSong` (audit 2026-04-16 §9.1) the preview produced uncompilable C#: signature glued to body brace, parameter-separator spaces stripped (`item,User? user`). New `BuildParameterListFromTextChange` helper serializes the existing parameters to text, applies the add/remove edit, and reparses the full list via `SyntaxFactory.ParseParameterList` — this produces correct comma + space trivia. The new list's enclosing trivia is copied from the original via `WithTriviaFrom`, preserving the close-paren's trailing whitespace. `PreviewRemoveParameterAsync` uses the same helper. The `updateCallsite` insert path also propagates a leading space on inserted arguments so positional callsites get `Compute(10, 20, 30)` not `Compute(10, 20,30)`. Dead `paramText` local at `ChangeSignatureService.cs:75` is now consumed by the `ParseParameter` path. The unused `ParameterListFromText` helper (and its `System.Collections.Immutable` using) was removed.
- **`format_range_preview` no longer returns empty diff for dirty input (`format-range-preview-empty-diff-compile-check-filter-false-clean`, `dr-9-12-flag-format-range-empty-returns-empty-diff-on-d`).** `RefactoringService.PreviewFormatRangeAsync` previously called `Formatter.FormatAsync(document, [span], …)` — the range-restricted overload silently dropped formatting edits whose target trivia sat outside the explicit span (NetworkDocumentation audit §9.5; firewall-analyzer §9.12). Result: preview returned a `unifiedDiff` with headers and no `@@` hunks while a subsequent `format_range_apply` shared the same stored (no-op) solution — the empty preview led callers to believe nothing would change. Replaced with whole-document format + line-level splice via the new `SpliceFormattedRange` helper: the formatter sees full context (no boundary truncation), and the splice keeps lines outside `[startLine, endLine]` byte-identical to the caller's input. Apply now always matches what the preview shows. Regression coverage: `FormatRangePreview_DirtyRange_EmitsNonEmptyDiff_ApplyMatches` (dirty range produces non-empty hunks; apply mutates exactly those lines) and `FormatRangePreview_DirtyOutsideRange_LeavesUnrelatedRegionAlone` (dirty section outside the requested range stays untouched). (Note: Defect B from the P3 row — `compile_check` filter false-clean — was closed in v1.18.2 by `compile-check-zero-projects-claimed-success-after-reload`.)
- **Apply path now creates only the files shown in the preview (`severity-critical-fail-preview-diff-does-not-match-t`, `dr-9-2-leaves-duplicate-type-in-source-file-and-creates`, `dr-9-2-writes-the-new-type-file-to-two-locations`).** `move_type_to_file`, `extract_type`, `extract_interface`, and cross-project variants called `Project.AddDocument` / `Solution.AddDocument` with a `filePath` argument but without `folders`. MSBuildWorkspace's `TryApplyChanges` then wrote a second copy at `{projectDir}/{fileName}` (project root) in addition to our explicit write at the intended deep path — NetworkDocumentation audit §9.2 reproduced this as ~90 CS0101 / CS0229 errors after apply. Fix: new `ProjectMetadataParser.ComputeDocumentFolders` helper computes the relative-segment list; every `AddDocument` call site (6 total across 4 services) now passes it. Regression test `MoveTypeDiskStateTests` explicitly asserts no rogue project-root copy.
- **`revert_last_apply` now restores created + deleted files (`severity-high-fail-documented-semantic-is-restore-pr`, `dr-9-3-leaves-files-created-by-the-apply-on-disk`, `dr-9-13-does-not-delete-files-created-by-the-reverted-a`).** `RefactoringService.ApplyRefactoringAsync` captured only the Solution snapshot before apply — no `FileSnapshotDto` list. UndoService fell back to the solution-based legacy path, which could not reverse file creation (created files remained on disk as untracked content) or file deletion (deleted files were not restored). Fix: compute an authoritative `FileSnapshotDto` list from `SolutionChanges` for added documents (`OriginalText: null` → delete on revert), removed documents (`OriginalText: <pre-apply disk bytes>`), and changed documents. UndoService's fast path (`RevertFromFileSnapshotsAsync`) now handles all three cases uniformly. `UndoTools` description updated — the "file create/delete/move…not revertible" caveat is gone.
- **SDK-style csprojs no longer gain duplicate `<Compile>` items on apply (`severity-medium-breaks-msbuild-until-csproj-is-hand`, `dr-9-1-add-items-that-break-sdk-style-projects`, `dr-9-6-bug-compile-include-adds-explicit-on-sdk-auto-in`).** MSBuildWorkspace's `TryApplyChanges`, when it sees an added document in an SDK-style csproj with default `<Compile>` globbing (the .NET 6+ default), injects an explicit `<Compile Include="…"/>` entry. Because the SDK glob already matches every `.cs` under the project directory, the injection produces `Duplicate 'Compile' items were included` on the next `workspace_reload`, breaking `dotnet build` / CI until hand-edited. Fix: new `ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems` helper detects the SDK attribute (attribute form, element form, import form) with an explicit opt-out via `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`. `RefactoringService.PersistDocumentSetChangesAsync` snapshots csproj content BEFORE `TryApplyChanges` for each SDK-style project with added documents and restores the snapshot AFTER — the in-memory workspace still has the added document, disk csproj stays byte-identical, and the next reload picks up the new file via the SDK glob automatically.
- **Generated interface files emit the using directives their members actually need (`severity-high-fail-produces-code-that-does-not-compi`, `dr-9-1-emits-interface-file-without-required-directive`).** `InterfaceExtractionService.FilterRelevantUsings` decided which source-file using directives to carry into the generated interface file by text-grepping the interface for the namespace's full name (non-System) or short name (System.*). But `BuildInterfaceMembers` renders types with `MinimallyQualifiedFormat` — short names like `NetworkInventory`, never the fully-qualified `NetworkDocumentation.Core.Models.NetworkInventory`. The grep for the full namespace string never matched, so the required using was dropped, and post-apply compilation failed with CS0246 / CS0535 on every use of the short name (NetworkDocumentation audit §9.1 repro for `DiagramService` → `IDiagramService`). Fix: replace the text-grep with a semantic walker (`CollectReferencedNamespaces`) that inspects the ISymbol typed shape of every candidate member and recursively walks generic type arguments + array element types so `Task<IReadOnlyList<Item>>` contributes `Task`, `IReadOnlyList`, and `Item` namespaces. Source-file aliases, static usings, and global usings are preserved; plain usings the walker determined are unneeded are dropped. Results sorted PEP-style.
- **`extract_method` no longer fabricates a `this` parameter (`extract-method-preview-fabricates-this-parameter`).** `DataFlowsIn` includes the implicit `this` pointer when the extracted region reads instance state via unqualified member access. Roslyn models `this` as `IThisParameterSymbol` (subtype of `IParameterSymbol`) with `Name = "this"` and `Type = <containing type>`. The filter `s is IParameterSymbol` accepted it, and the rendered parameter list became `(MusicManager this, Audio item, …)` (Jellyfin stress test §5 repro on `MusicManager.GetInstantMixFromSong`). Fix: filter rejects `IParameterSymbol.IsThis == true`. The extracted method is always declared on the same containing type (isStatic derived from the enclosing member), so `this` is implicitly available.
- **`compile_check` fails loud on zero-projects false-green (`compile-check-zero-projects-claimed-success-after-reload`).** `CompileCheckService.CheckAsync` built `Success = errorCount == 0 && !cancelled`, with no guard on `completedProjects`. When the filter (or the auto-reload path under `ROSLYNMCP_ON_STALE=auto-reload`) left the project list empty, the foreach body never ran, `errorCount` stayed 0, and Success trivially flipped true — the false-green reproduced in SampleSolution audit §9.8 (success:true, completedProjects:0, totalProjects:0 while the workspace still had real errors). Fix: `Success` now requires `completedProjects > 0`. When `projectList.Count == 0` the response includes a structured `RestoreHint` distinguishing "filter matched zero projects" (names the filter, suggests case-sensitivity) from "silent empty project set after reload race" (directs to `workspace_reload` / `workspace_load`).
- **Per-workspace caches drop entries synchronously on reload (`compile-check-stale-assembly-refs-post-reload`).** `CompilationCache`, `DiagnosticService`, `DiRegistrationService`, and `NuGetDependencyService` all invalidated their per-workspace entries on the existing `WorkspaceClosed` event. None subscribed to any reload signal because none existed — the per-read version check caught stale entries eventually, but created a narrow window where a just-returned `Compilation` handle could be stale until the next cache read. That window is the specific shape reported in the 2026-04-15 IT-Chat-Bot audit (193 spurious CS1705 after `workspace_reload`, real `dotnet build` surfaced only 2 pre-existing errors). Fix: new `IWorkspaceManager.WorkspaceReloaded` event, raised by `ReloadAsync` via a `RaiseWorkspaceReloaded` wrapper that swallows handler exceptions (matching the existing `RaiseWorkspaceClosed` contract). All four caches subscribed; test fakes in 6 files updated with no-op add/remove implementations. `ai_docs/runtime.md` gains a Known-issues entry documenting the deeper `dotnet restore` + `workspace_reload` drift scenario and the recommended recovery (close + re-load to recreate the MSBuildWorkspace).

### Added

- **`validate_workspace summary=true` mode (`validate-workspace-output-cap-summary-mode`).** Tool returned 135 KB on Jellyfin's 40-project solution (audit 2026-04-16 §8). Added optional `summary: bool = false` to `IWorkspaceValidationService.ValidateAsync` + the `validate_workspace` tool. When `summary=true`: drops the per-diagnostic `ErrorDiagnostics` list and per-test `DiscoveredTests` list. `OverallStatus`, `WarningCount`, `ChangedFilePaths`, `CompileResult` (which has its own bounded summary), `DotnetTestFilter`, and `TestRunResult` (aggregate counters only) still surface — callers wanting per-item detail re-run with `summary=false` or call the underlying primitive directly.
- **`symbol_impact_sweep summary=true` + `maxItemsPerCategory` (`symbol-impact-sweep-output-size-blowup`).** v1.17 composite tool was unusable on high-fan-out symbols — Jellyfin's `BaseItem` (1452 refs, 32 derived types) returned ~886 KB and exceeded the MCP cap on the first call (stress test 2026-04-15 §4b). Added optional `summary: bool = false` + `maxItemsPerCategory: int?` to `IImpactSweepService.SweepAsync` and the `symbol_impact_sweep` tool. `summary=true` forwards to `IReferenceService.FindReferencesAsync` so per-ref preview text is dropped at the source (saves materializing it just to discard later). `maxItemsPerCategory` truncates References, MapperCallsites, and SwitchExhaustivenessIssues independently — a 1500-ref symbol with 0 mapper callsites still returns the mapper-callsite list intact. Persistence findings are not capped (always small in practice; qualitatively distinct review work).
- **`get_syntax_tree` gains `maxNodes` and `maxTotalBytes` budgets (`get-syntax-tree-max-output-chars-incomplete-cap`).** `maxOutputChars` was documented as leaf-text-only but agents tuned it expecting a total-response cap; structural JSON (kinds, positions, nesting) dominated and produced 229 KB on Jellyfin `EncodingHelper.cs` with `maxOutputChars=20000` (stress test 2026-04-15 §3). `SyntaxBudget` extended with three independent counters: `RemainingChars` (leaf-text only — original semantics), `RemainingNodes` (caps total node count, default 5000), `RemainingBytes` (caps estimated total response size at ~120 bytes per node + leaf text length, default 65536). Walker stops at the first cap hit and emits a single `TruncationNotice` whose text now lists all three caps. Tool description rewritten to clarify that `maxOutputChars` is leaf-text-only and direct callers to `maxTotalBytes` for total-response control (this is a documented behavior change but not a runtime breaking change for code that already sized `maxOutputChars` defensively).
- **`get_nuget_dependencies summary=true` mode (`get-nuget-dependencies-no-summary-mode`).** Tool returned ~102 KB on Jellyfin's 40-project solution and exceeded the MCP cap (stress test 2026-04-15 §4). Added optional `summary: bool = false` to `INuGetDependencyService.GetNuGetDependenciesAsync` and the `get_nuget_dependencies` tool. New `NuGetPackageSummaryDto` (`{packageId, version, projectCount, distinctVersionCount}`) + `Summaries` field on `NuGetDependencyResultDto`. When `summary=true`: per-package summary populated, verbose `Packages` + `Projects` arrays emitted as empty (callers iterate without null checks). `DistinctVersionCount > 1` flags version-drift hazards worth investigating.
- **`find_references summary=true` mode (`find-references-preview-text-inflates-response`).** `find_references(IUserManager)` returned ~154 KB on Jellyfin's 233-ref symbol because per-ref `PreviewText` dominated the payload — `limit` capped the ref count but not the response size (Jellyfin stress test 2026-04-15 §7). Added optional `summary: bool = false` to `IReferenceService.FindReferencesAsync`, `ReferenceLocationMaterializer.MaterializeDtosAsync`, and the `find_references` tool. With `summary=true`, each `LocationDto` has `PreviewText = null`; `FilePath`, `StartLine`, `StartColumn`, `ContainingMember`, and `Classification` stay populated. Default `summary=false` preserves the v1.18.2 response shape — existing callers see no change. New top-level `summary` field added to the tool's response envelope so callers can audit which mode produced the result.
- **`rename_preview` `summary=true` mode (`rename-preview-output-cap-high-fan-out-symbols`).** `rename_preview` on `IUserManager` (233 refs) returned ~98 KB of unified-diff text and exceeded the MCP output cap — the #1 finding in the 2026-04-15 Jellyfin stress test. New optional `summary: bool = false` parameter on the tool + service. When true, per-file `UnifiedDiff` is replaced with a compact single-line marker (`# summary=true: <oldLines> → <newLines> lines (±<net>). Full unified diff suppressed …`). The stored preview Solution still carries every real edit, so a subsequent `rename_apply` rewrites all references correctly — summary is purely a payload-size optimization on the caller-facing response. Added/removed documents get their own minimal markers. Tests verify that apply on a summary-mode token still rewrites every reference.
- **Apply-truncation safety gate (`severity-high-output-would-ship-as-is-and-fail-code`, `actual-observed-i-interface-file-identical-corruptio`).** `SolutionDiffHelper.ComputeChangesAsync` truncated the returned diff list when it hit the 64 KB per-solution cap, appending a synthetic sentinel entry with `FilePath = "<truncated>"`. The stored Solution in `PreviewStore` still held the full change set, so apply silently produced changes the caller could not see in the preview — the "preview truncated while apply still mutates disk" concern from firewall-analyzer §9.2/§9.3 and NetworkDocumentation §9.2. Fix: new `SolutionDiffHelper.TruncatedSentinelFilePath` constant exposes the sentinel; `PreviewStore` gains a `DiffTruncated` flag with new `Store` overloads (explicit bool + convenience `IReadOnlyList<FileChangeDto>` that derives the flag from sentinel presence); `RefactoringService.ApplyRefactoringAsync` gains a second overload with `bool force`. When the stored preview has `DiffTruncated = true` and `force = false`, apply is refused with an actionable error explaining how to re-run with a narrower scope or force the blind apply. 10 preview-producing services updated to pipe the changes list through `PreviewStore.Store` so truncated previews are tagged at the source.
- **`metadataName` parameter on 14 locator-based tools (`find-references-metadataname-parameter-rejected`, `dr-9-3-rejects-only-invocations`).** `SymbolResolver.ResolveAsync` fully supported `metadataName` (via `ResolveByMetadataNameAsync`), but the tool surface arbitrarily disabled it on 14 of the locator-based tools by passing `supportsMetadataName: false` into `SymbolLocatorFactory.Create`. Agents with a fully-qualified type name (from DI registrations, `get_symbol_outline`, `find_unused_symbols`) had to fall back to `Grep` — a 5th documented reproduction appeared in the 2026-04-15 IT-Chat-Bot audit. Tools opened up: `go_to_definition`, `find_references`, `find_overrides`, `find_base_members`, `member_hierarchy`, `find_property_writes`, `goto_type_definition`, `type_hierarchy`, `callers_callees`, `impact_analysis`, `find_type_mutations`, `find_shared_members`, `rename_preview`, `test_related`.
- **`SyntaxFormatter` helper (infrastructure).** `src/RoslynMcp.Roslyn/Helpers/SyntaxFormatter.cs` centralizes `Formatter.FormatAsync` plumbing for future formatter swaps across the FORMAT-BUG-001/002/004/005/006 family. Not consumed in this release — shipped as infrastructure so follow-up work on specific formatter-bug repros has a shared entry point.

### Changed — BREAKING

- **`isReady` contract tightened; new `analyzersReady` field; transient-stale `restoreHint` (`dr-9-7-reports-during-refactoring-transitions-but-the-w`, `severity-flag-soft-inconsistency-does-not-block-work`, `workspace-load-isready-misreports-unresolved-analyzers`).** `WorkspaceStatusSummaryDto.From` (`src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs:52`) computed `isReady` only from `errors == 0`, letting `WORKSPACE_UNRESOLVED_ANALYZER` warnings pass through while every analyzer-driven tool was silently missing findings (Jellyfin 2026-04-16: `isReady=true` with 40 unresolved-analyzer warnings against the custom analyzer project). Conversely, transient stale during refactor flipped `isReady=false` indistinguishable from broken state (NetworkDocumentation §9.7). New `AnalyzersReady: bool` field is computed independently — `false` iff any `WORKSPACE_UNRESOLVED_ANALYZER` warning is present. `IsReady` now requires `IsLoaded && !IsStale && AnalyzersReady && errors == 0`. `BuildRestoreHint` extended with two new branches: (1) unresolved-analyzer warnings → "N analyzer reference(s) failed to load — analyzer-driven tools will under-report"; (2) stale + zero errors → "Workspace is stale but has no errors — likely transient (post-apply settling). Retry `workspace_status` in ~250ms; if still stale, run `workspace_reload`." **BREAKING** — clients parsing the previous `WorkspaceStatusSummaryDto` shape must add the `analyzersReady` field; positional record-pattern matching breaks.
- **`SymbolLocatorFactory.Create` signature — `supportsMetadataName` parameter removed.** The parameter existed only to tailor an error message for tools that lacked `metadataName` exposure; with all 14 callers now supporting it, the parameter is vestigial. The unified error message advertises all three resolution strategies (filePath+line+column, symbolHandle, metadataName). Internal API — no external-agent impact.
- **`IPreviewStore.Store` — new overloads.** Original single-form overload retained for backwards compatibility (assumes `diffTruncated = false`). New overloads accept `bool diffTruncated` explicitly or derive it from an `IReadOnlyList<FileChangeDto>` via sentinel detection. **`IPreviewStore.Retrieve` return type changed** — the named-tuple now includes `bool DiffTruncated`. Callers that deconstructed the old 4-tuple need to add the 5th field; see `RefactoringService.ApplyRefactoringAsync` for the canonical pattern.
- **`IRefactoringService.ApplyRefactoringAsync` — new overload with `bool force`.** Original single-form overload retained (forwards to the new overload with `force = false`). When `force = false` and the stored preview's `DiffTruncated` flag is set, apply returns a structured error. Legacy callers keep their previous behavior; callers that explicitly need to apply a truncated preview can now do so by passing `force: true`.
- **`IRefactoringService.PreviewRenameAsync` — new overload with `bool summary`.** Original single-form overload retained (forwards to `summary = false`). Tool-schema changes (optional `summary` param on `rename_preview`) are backwards-compatible — omitting the parameter preserves v1.18.2 response shape.

### Maintenance

- **Backlog:** 21 rows closed across the v6 top-10 pass and the 2026-04-16 backlog sweep:
  - **Top-10 v6 pass (PR #162) — 19 rows:**
    - **P2 (7):** `severity-critical-fail-preview-diff-does-not-match-t`, `severity-high-fail-documented-semantic-is-restore-pr`, `severity-high-fail-produces-code-that-does-not-compi`, `severity-high-output-would-ship-as-is-and-fail-code`, `severity-medium-breaks-msbuild-until-csproj-is-hand`, `actual-observed-i-interface-file-identical-corruptio`, `rename-preview-output-cap-high-fan-out-symbols`.
    - **P3 (6):** `compile-check-stale-assembly-refs-post-reload`, `compile-check-zero-projects-claimed-success-after-reload`, `dr-9-1-add-items-that-break-sdk-style-projects`, `dr-9-2-leaves-duplicate-type-in-source-file-and-creates`, `extract-method-preview-fabricates-this-parameter`, `find-references-metadataname-parameter-rejected`.
    - **P4 (6):** `dr-9-1-emits-interface-file-without-required-directive`, `dr-9-13-does-not-delete-files-created-by-the-reverted-a`, `dr-9-2-writes-the-new-type-file-to-two-locations`, `dr-9-3-leaves-files-created-by-the-apply-on-disk`, `dr-9-3-rejects-only-invocations`, `dr-9-6-bug-compile-include-adds-explicit-on-sdk-auto-in`.
  - **Backlog sweep 2026-04-16 — 17 rows so far:**
    - **P3 (11):** `format-range-preview-empty-diff-compile-check-filter-false-clean`, `change-signature-preview-brace-concat-on-add-op`, `dr-9-7-reports-during-refactoring-transitions-but-the-w`, `find-references-preview-text-inflates-response`, `change-signature-preview-callsite-summary`, `get-nuget-dependencies-no-summary-mode`, `get-syntax-tree-max-output-chars-incomplete-cap`, `symbol-impact-sweep-output-size-blowup`, `validate-workspace-output-cap-summary-mode`, `dr-9-1-does-not-update-external-consumer-call-sites`, `mcp-parameter-validation-error-messages`.
    - **P4 (6):** `dr-9-12-flag-format-range-empty-returns-empty-diff-on-d`, `severity-flag-soft-inconsistency-does-not-block-work`, `workspace-load-isready-misreports-unresolved-analyzers`, `server-info-update-latest-inverted`, `mcp-stdio-console-flush-on-exit`, `file-resource-uri-windows-path-handling`.
- **Tests:** 497 → 564 (+67 across 21 new regression test files; +1 added inline to `ChangeSignaturePreviewTests`; existing `ExtractType_FromAnimalService_CreatesPreview` rewritten to assert the new refusal, plus a new `ExtractType_NoExternalConsumers_ProducesPreview` companion). Each item ships with a targeted regression suite covering the specific audit repro. Zero test regressions; zero build warnings.
- **Plan / audit intake:** New file `ai_docs/plans/20260415T220000Z_top10-remediation-plan.md` (the source-code-verified plan drafted before the implementation pass). New audit file `ai_docs/audit-reports/20260415T193603Z_jellyfin_stress-test.md`. Both linked from `ai_docs/backlog.md` Refs. Backlog-sweep plan: `ai_docs/plans/20260416T054040Z_backlog-sweep/plan.md` (44 initiatives total; per-initiative shipping in progress).

## [1.18.2] - 2026-04-15

Plugin install path finally works on a default Claude Code install.

**This release supersedes v1.18.1, which shipped with the wrong diagnosis.** v1.18.1 split the plugin-shipped `.mcp.json` into wrapper / no-wrapper variants and added a defensive `ReadEnv` helper — both were no-ops. Binary disassembly of Claude Code 2.1.101 (`claude.exe` offset `125626186`, `app.asar` function `fRe` offset ~`2682541`) shows:

1. The desktop-app plugin loader accepts **both** `.mcp.json` shapes via `p = f.mcpServers ?? f`, so the format split was never the bug.
2. The CLI SDK's `${user_config.*}` resolver is a REQUIRED-mode function that **throws** `Missing required user configuration value: {K}` during plugin-config resolution — **before** the server process is spawned. On any install flow that skips the enable-time user-config prompt (automation, `--allow-dangerously-skip-permissions`, pre-existing installs, `bypassPermissions` mode — i.e. most default installs), none of the `${user_config.ROSLYNMCP_*}` references in the plugin's `.mcp.json` resolve. The SDK throws, no server starts, no error surfaces.
3. The v1.18.1 `ReadEnv` defensive helper sits inside the server process and is therefore unreachable in this failure mode. (It's kept as defense-in-depth — harmless, clear intent.)

### Fixed

- **Plugin-scope MCP registration now works on a default install.** Dropped the `env` block from both plugin-shipped `.mcp.json` files (`.claude-plugin/mcp.json` and the repo-root `.mcp.json`). The server uses the compiled-in defaults already declared in `Program.cs`'s `Options` initializers (Max Workspaces = 8, Build Timeout = 300s, Test Timeout = 600s, Rate Limit = 120/60s, Request Timeout = 120s, etc.). No `${user_config.*}` substitution runs, so `QkH` has nothing to throw on. Fresh install + fresh cwd + no project-scope `.mcp.json` now registers the `mcp__roslyn__*` tools correctly. Closes `dr-9-1-high-roslyn-plugin-mcp-does-not-connect-in-audit`.

### Changed — BREAKING

- **Removed `userConfig` block from `.claude-plugin/plugin.json`.** The five `ROSLYNMCP_*` entries previously exposed to Claude Code's plugin enable-time prompt are gone. The right abstraction for these values is project-scope (per-repo) `.mcp.json`, not global per-user user-config — different repos need different timeouts and workspace limits, and the enable-time prompt was unreliable in the first place. Users who had values configured via plugin user-config should migrate them to a project-scope `.mcp.json` (template: [`docs/mcp-json-examples/with-overrides.mcp.json`](docs/mcp-json-examples/with-overrides.mcp.json)).
- **Removed `user_config` block from `manifest.json` (DXT manifest).** Same reasoning. The compiled-in defaults cover every published `ROSLYNMCP_*` knob.

### Added

- **`docs/mcp-json-examples/`** — three files documenting the project-scope override pattern: `README.md` (explains when to use which), `minimal.mcp.json` (just the command), and `with-overrides.mcp.json` (full `env` block with literal values for every documented `ROSLYNMCP_*` knob). This is the canonical template for tuning users.
- **README updates** — both `README.md` and `src/RoslynMcp.Host.Stdio/README.md` (the NuGet `PackageReadmeFile` rendered on nuget.org) now explicitly say "no further configuration is required" after `/plugin install` and link to the examples directory.

### Migration

- **Fresh install, no prior `.mcp.json`:** no action. Works out of the box.
- **Project-scope `.mcp.json` with literal `env` values:** no action. Project-scope loader does straight string pass-through; `${user_config.*}` is not involved.
- **Project-scope `.mcp.json` copied from this repo's v1.18.1 or earlier source with `${user_config.*}` references:** strip the `env` block or replace the `${user_config.*}` values with literals. Those substitutions were never resolving in end-user installs — pretending they worked was the bug this release fixes.
- **Plugin user-config values set via an enable-time prompt:** those are now ignored at plugin scope. Set them via project-scope `.mcp.json` instead.

### Maintenance

- **Backlog:** closed `dr-9-1-high-roslyn-plugin-mcp-does-not-connect-in-audit` (root cause shipped). Updated `mcp-connection-session-resilience` to remove the `${user_config.*}` substitution narrative (now known to be a definitionally broken API surface, not a resolution-pipeline gap).
- **Jellyfin audit report:** added a third supersede banner. The first banner said the fix was the format split; the second was shipped in v1.18.1. The third correctly names the root cause (SDK-level `QkH` throw on unresolved `${user_config.*}`) and points at v1.18.2 as the real fix.
- **Retired task brief.** The binary-disassembly evidence in the retired `ai_docs/tasks/fix-plugin-mcp-json-format.md` is preserved in git history per archive policy.

## [1.18.1] - 2026-04-15

Plugin-packaging fix — the marketplace install path now loads `roslyn-mcp` correctly on any repo. Zero server behaviour change; this is a pure plugin-loader / env-resolution hardening pass.

### Security

- **`System.Security.Cryptography.Xml` transitive bump to 10.0.6.** 8.0.0 was pulled in transitively via `Microsoft.Build.*` / `Microsoft.CodeAnalysis.Workspaces.MSBuild` and carries two High-severity DoS advisories against the `EncryptedXml` class (`GHSA-37gx-xxp4-5rgx`, `GHSA-w3x6-4m5h-cxqf`, `CVE-2026-26171`). Mitigated with a direct `PackageReference` in `RoslynMcp.Roslyn.csproj` — NuGet's central-package-management transitive pinning would have cascaded into an MSBL001 collision with `Microsoft.Build.Locator`'s runtime-asset constraint, so the surgical direct reference is the right scope. `dotnet list package --vulnerable --include-transitive` reports clean.

### Fixed

- **Plugin-scope `.mcp.json` format (`dr-9-1-high-roslyn-plugin-mcp-does-not-connect-in-audit`, `mcp-connection-session-resilience`):** The `.mcp.json` file bundled inside the plugin now uses the top-level server-name shape (`{ "roslyn": {...} }`) that Claude Code's plugin loader expects, instead of the `{ "mcpServers": { "roslyn": {...} } }` wrapper that is only valid for project-scope and user-scope configs. Users installing via the marketplace no longer need a repo-local `.mcp.json` fallback for roslyn tools to register. The project-scope file at repo root (`/.mcp.json`) keeps the wrapper shape so dogfooding in this repo still works.
- **Unresolved `${user_config.*}` env placeholders (`\u00a75.4` of the task brief):** When a user hasn't configured a Claude Code plugin user-config entry, Claude Code passes the literal `${user_config.KEY}` string as the env var value rather than substituting or omitting it. `Program.cs` now routes every `ROSLYNMCP_*` read through a single `ReadEnv(name)` helper that detects the literal placeholder shape, logs one line to stderr naming the key, and falls back to the in-source default. Previously, every `int.TryParse` / `bool.TryParse` call silently rejected the literal with no signal; now the behaviour is explicit. Supersedes the `${user_config.*}` substitution-is-broken hypothesis from the jellyfin audit report.

### Changed

- **Plugin manifest `mcpServers` path:** `.claude-plugin/plugin.json` now points to `./.claude-plugin/mcp.json` (new file, plugin-scope shape) instead of `./.mcp.json` (repo-root file, project-scope shape). Two files, two roles — no duplication of concerns.
- **Env-var binding path in `Program.cs`:** All 22 `Environment.GetEnvironmentVariable("ROSLYNMCP_*")` call sites now go through `ReadEnv(name)`. Code shape unchanged; defensive filter transparent to all valid values.

### Maintenance

- **Tests:** Full suite re-run, all pass.
- **Backlog:** Closed `dr-9-1-high-roslyn-plugin-mcp-does-not-connect-in-audit`. Updated `mcp-connection-session-resilience` to strike the `${user_config.*}` substitution narrative (now known to be a loader-skip issue, not a resolution-pipeline issue). Retired the brief `ai_docs/tasks/fix-plugin-mcp-json-format.md` — per archive policy, completed task briefs are not retained in-tree; recover from git history if needed.
- **Jellyfin audit report:** Added a top-of-file supersede note pointing to this release as the root-cause fix.

## [1.18.0] - 2026-04-14

Top-10 backlog remediation pass v5 — **four new tools**, one new prompt, one new skill, two service extensions, a security-analyzer cleanup, and the full v1.17 catalog-attribute migration. Stable surface unchanged at 102; experimental lifts from 36 → 40 (live `server_info` confirms 142 tools post-ship; CHANGELOG initially miscounted as 144).

### Removed

- **`SecurityCodeScan.VS2019` (`securitycodescan-currency`):** archived package (last release 2021) deleted from `Directory.Build.props` + `Directory.Packages.props`. `Microsoft.CodeAnalysis.NetAnalyzers 10.0.100` (already installed) covers the equivalent CA-rule security checks. Build and test verification clean. The `analyzer_status` tool now reports `securityCodeScanPresent: false` for this workspace — existing tests updated accordingly.

### Added

- **`change_signature_preview` (experimental):** Plumbs Roslyn's signature-change refactoring through the MCP surface. Supports `op=add` (insert parameter with default; splices `default value` at every callsite; inserts named arg when caller uses named args), `op=remove` (drop parameter with callsite cleanup), and `op=rename` (delegates to the rename engine for parameter rename with named-arg callsite safety). `op=reorder` is reserved for a follow-up release. Closes `change-signature-add-parameter-cross-callsite`.
- **`symbol_refactor_preview` (experimental):** Composite preview chaining `rename` + `edit` + `restructure` operations in order. Each operation sees the rewritten state from earlier ops; the final accumulator is stored under one preview token. Max 25 operations / 500 affected files per preview. Closes `agent-symbol-refactor-unified-preview`.
- **`validate_workspace` (experimental):** One-call composite that runs `compile_check` + error-severity diagnostics + `test_related_files` (+ optional `test_run`). Emits an aggregate envelope with `overallStatus` = `clean` / `compile-error` / `analyzer-error` / `test-failure`. Reduces 4 round-trips to 1. Closes `roslyn-mcp-post-edit-validation-bundle`.
- **`get_prompt_text` (experimental):** Generic dispatcher that exposes every `[McpServerPrompt]`-registered prompt as a `call_mcp_tool`-invocable tool. Pass `promptName` + a JSON object of the prompt's parameters; returns the rendered messages array. Closes `prompt-tools-exposable-to-agents` for hosts that can't invoke prompts via `prompts/get`.
- **`refactor_loop` MCP prompt + `refactor-loop` Claude skill:** Paired surface that walks agents through the standard refactor → preview → apply-with-verify → validate_workspace loop using v1.17/v1.18 primitives. Closes `roslyn-mcp-guided-refactor-flow`. The skill lives under `skills/refactor-loop/SKILL.md`; the prompt lives in `RoslynPrompts.RefactoringWorkflows.cs`.

### Changed

- **Catalog attribute migration (`mcp-tool-catalog-attribute-binding`, v1.17 Item 3 finish):** Every `[McpServerTool]` method now carries a matching `[McpToolMetadata(category, tier, readOnly, destructive, summary)]` attribute — 131 mechanical additions via the new `eng/gen-mcp-tool-metadata.ps1` generator, plus 7 already added in v1.17. `SurfaceCatalogTests.McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` is now strict: missing attribute OR field-level drift fails CI. Silent drift between `[McpServerTool]` and `ServerSurfaceCatalog.Tools` is now structurally impossible.
- **`test_reference_map` mock-drift detection (`test-mock-drift-new-interface-usage`):** Response DTO gains a `mockDriftWarnings` array. Each entry identifies an NSubstitute-mocked interface method that production code calls but the matching test class never stubs via `.Returns()` / `.ReturnsForAnyArgs()` / `.Configure*()`. Heuristic: NSubstitute only; Moq/FakeItEasy not detected. Does not flood results when no test project uses NSubstitute.
- **`symbol_impact_sweep` persistence-layer findings (`mapper-snapshot-dto-symmetry-check`):** When the swept symbol is a property, the response gains a `persistenceLayerFindings` array. Each entry pairs the domain property with its sibling DTO record (matched by `JsonPropertyName` / `DataMember` attribute or by property name), then checks mapper-suffixed types for `To*` + `From*` symmetry. Asymmetric pairs — e.g. `ToSnapshot` writes the property but `FromSnapshot` never reads it — are flagged.
- **Composite preview token disk persistence (`preview-token-cross-process-handoff`):** New env var `ROSLYNMCP_PREVIEW_PERSIST_DIR` activates opt-in on-disk storage for `CompositePreviewStore` tokens under `{dir}/{workspaceVersion}/{token}.json` with atomic-write + TTL-by-mtime semantics. When unset (default), behavior is in-memory only as before. Only composite preview tokens are portable cross-process — full Roslyn-`Solution` previews stored in `IPreviewStore` remain single-process.
- **`IEditService` / `IChangeTracker` consumption:** `WorkspaceValidationService` reads `IChangeTracker.GetChanges()` when the caller doesn't pass an explicit `changedFilePaths` list, so the post-edit validation bundle auto-scopes to the session's tracked mutations.
- **Surface counts:** catalog bumps from 138 → 142 tools (102 stable / 40 experimental, +4 experimental). The 4 new tools: `get_prompt_text`, `validate_workspace`, `change_signature_preview`, `symbol_refactor_preview`. `preview-token-cross-process-handoff` shipped as an env var (no new tool surface) and `roslyn-mcp-guided-refactor-flow` shipped as a prompt + skill. Prompts bump from 19 → 20 (new `refactor_loop`). Skills bump from 19 → 20 (new `refactor-loop`). (CHANGELOG originally claimed 144/+6; corrected post-ship after `server_info` reported the live count.)

### Maintenance

- **Backlog:** Closed 10 rows in this pass plus the `securitycodescan-currency` sidecar (`roslyn-mcp-workspace-staleness`… wait, those closed in v1.17). v1.18 closes: `securitycodescan-currency`, `tool-catalog-full-attribute-migration`, `change-signature-add-parameter-cross-callsite`, `prompt-tools-exposable-to-agents`, `roslyn-mcp-post-edit-validation-bundle`, `preview-token-cross-process-handoff`, `agent-symbol-refactor-unified-preview`, `roslyn-mcp-guided-refactor-flow`, `test-mock-drift-new-interface-usage`, `mapper-snapshot-dto-symmetry-check`. The `composite-split-service-di-registration` P3 row is not closed but is now implementable on top of `symbol_refactor_preview` — deferred to a future pass.
- **Tests:** 491 passed, 0 failed. `SecurityDiagnosticIntegrationTests.AnalyzerStatus_AfterSecurityCodeScanRemoval_ReportsAbsence` replaces the pre-v1.18 `AnalyzerStatus_Reports_SecurityCodeScan_Present` to match the removal. `SurfaceCatalogTests.McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` now enforces the attribute across all 144 tools.
- **Docs:** `ai_docs/runtime.md` env-var table adds `ROSLYNMCP_PREVIEW_PERSIST_DIR`. `ai_docs/backlog.md` synced per the workflow contract.
- **CHANGELOG correction:** v1.17.0 prose originally claimed 140 tools / +9 experimental; actual live count was 138 / +7. v1.17.0 entry rewritten in this PR to match reality; v1.18.0 count is 138 → 142 (+4) derived from the corrected baseline, after a second post-ship correction when `server_info` revealed a similar over-count.

## [1.17.0] - 2026-04-14

Top-10 backlog remediation pass v4 — **seven new tools**, one correctness gate, and a metadata-drift safeguard. Stable surface unchanged at 102; experimental lifts from 29 → 36 (live `server_info` confirms 138 tools post-ship; CHANGELOG initially miscounted as 140).

### Added

- **`preview_multi_file_edit` + `preview_multi_file_edit_apply` (experimental):** Multi-file edit batch gets a preview tier. `EditService.PreviewMultiFileTextEditsAsync` simulates every file's edits against one Roslyn `Solution` snapshot, computes per-file unified diffs, and stores the snapshot in `IPreviewStore`. Redeem via the new `_apply` mirror. Unblocks `agent-symbol-refactor-unified-preview`, `change-signature-add-parameter-cross-callsite`, and `composite-split-service-di-registration`.
- **`restructure_preview` (experimental):** Syntax-tree pattern-based find-and-replace. Pattern and goal are parsed as C# expressions or statements with `__name__` placeholder captures; `StructuralRewriter` matches on kind + structural equivalence, then substitutes captured sub-expressions back into the goal. Closes `roslyn-structural-search-and-replace`.
- **`replace_string_literals_preview` (experimental):** Rewrites `StringLiteralExpressionSyntax` nodes to a named constant/identifier expression, but **only** when the literal is in argument / initializer / attribute-argument / return position — XML doc comments, interpolated-string holes, and `nameof()` literals are not touched. Auto-injects `using` directives. Closes `string-literal-to-constant-extraction`.
- **`scaffold_test_batch_preview` (experimental):** Batch wrapper around `scaffold_test_preview`. Reuses one compilation cache across N targets (eliminating per-target `GetCompilationAsync` cost) and emits one composite preview token covering every generated test file. Closes `scaffold-test-batch-mode`.
- **`symbol_impact_sweep` (experimental):** Runs three sub-queries in parallel after a symbol change: solution-wide references, non-exhaustive-switch diagnostics (`CS8509` / `CS8524` / `IDE0072`), and mapper/converter/serializer/translator/adapter-suffix callsites. Emits a structured `SymbolImpactSweepDto` with `SuggestedTasks` so reviewers get a ready-made checklist. Closes `mechanical-sweep-after-symbol-change`.
- **`test_reference_map` (experimental):** Static reference-based test coverage map. Walks each test project's `MethodDeclarationSyntax`, records every productive-symbol invocation via the semantic model, and returns `{ coveredSymbols, uncoveredSymbols, coveragePercent, inspectedTestProjects, notes }`. Fast alternative to the runtime `test_coverage` tool for "is this symbol tested at all" questions. Closes `static-test-reference-map`.
- **`McpToolMetadataAttribute` + drift guard (Item 3 partial):** New `[McpToolMetadata(category, tier, readOnly, destructive, summary)]` attribute in `src/RoslynMcp.Host.Stdio/Catalog`. New test `SurfaceCatalogTests.McpToolMetadata_WhenPresent_MatchesCatalogEntry` fails CI when any tool carrying the attribute drifts from its `ServerSurfaceCatalog.Tools` entry. Annotated on the 9 v1.17 tools as proof-of-concept; remaining ~122 tools queued as backlog row `tool-catalog-full-attribute-migration`. Closes `mcp-tool-catalog-attribute-binding`.

### Changed

- **`scaffold_type_preview` auto-implements interface base (`scaffold-implementation-from-interface`):** When `baseType` or any entry in `interfaces` resolves to an interface and the new `implementInterface` flag is true (default), the scaffolded class body now includes `NotImplementedException()` stubs for every interface member — methods (with generic constraints + parameter `ref`/`out`/`in`/`params` modifiers), properties (with `init` vs `set` detection), and events. Auto-injects required `using` directives for types referenced by the stubs. Skips DIM default-implementation members and static interface members. `ScaffoldTypeDto` gains `ImplementInterface` (default true). **Behavior change:** callers who scaffolded `class Foo : IBar` and relied on the previous empty body now get `throw new NotImplementedException()` stubs; pass `implementInterface: false` to restore the old shape.
- **`test_run` fast-fail on MSBuild file lock (`test-run-file-lock-fast-fail`):** `DotnetCommandRunner` gains an `EarlyKillPattern` streaming-match hook. `TestRunnerService` registers an MSB3027/MSB3021 regex so the child `dotnet test` is killed as soon as MSBuild prints the first retry line — the `FailureEnvelope` returns within ~200ms instead of ~10s after MSBuild's 10×1s retry loop completes. New `CommandExecutionDto.EarlyKillReason` field; `ClassifyNoTrxFailure` emits a distinct "terminated early" summary when the runner fast-fails. Opt-out: `ROSLYNMCP_FAST_FAIL_FILE_LOCK=false`.
- **Workspace staleness gate (`roslyn-mcp-workspace-staleness`):** `WorkspaceExecutionGate` now consults `IWorkspaceManager.IsStale` before every read/write call and acts per `ExecutionGateOptions.OnStale`: `auto-reload` (default — transparently reloads, stamps `GateMetricsDto.StaleAction = "auto-reloaded"` + `StaleReloadMs`), `warn` (proceeds against stale snapshot, stamps `StaleAction = "warn"`), or `off` (legacy no-op). New env var `ROSLYNMCP_ON_STALE`. New `IWorkspaceManager.IsStale(string)` surface. Four new tests in `WorkspaceExecutionGateTests`.
- **`IScaffoldingService` constructor signature:** now takes `IPreviewStore` to support the batch-mode composite preview. DI registration updated in `ServiceCollectionExtensions` and test infrastructure (`TestServiceContainer`).
- **`IEditService` interface:** new `PreviewMultiFileTextEditsAsync` method; all existing implementations (plus `SuppressionServiceTests.StubEditService`) updated.
- **`IDotnetCommandRunner` / `IGatedCommandExecutor`:** new overloads accept `IReadOnlyList<EarlyKillPattern>?`; default implementations delegate to the original overload so legacy callers keep compiling.
- **Surface counts:** catalog bumps from 131 → 138 tools (102 stable / 36 experimental, +7 experimental). Stable tier unchanged in this release. (CHANGELOG originally claimed 140/+9; corrected post-ship after `server_info` reported the live count.)

### Maintenance

- **Backlog:** Closed 11 rows in this pass (`roslyn-mcp-workspace-staleness`, `scaffold-implementation-from-interface`, `mcp-tool-catalog-attribute-binding`, `test-run-file-lock-fast-fail`, `roslyn-mcp-multi-file-preview-apply`, `roslyn-structural-search-and-replace`, `string-literal-to-constant-extraction`, `scaffold-test-batch-mode`, `mechanical-sweep-after-symbol-change`, `static-test-reference-map`, `roslyn-mcp-claude-direct-tool-registration` — last one verified already implemented via plugin `mcpServers` registration). Three P3 rows unblocked (`agent-symbol-refactor-unified-preview`, `change-signature-add-parameter-cross-callsite`, `composite-split-service-di-registration`). New row added: `tool-catalog-full-attribute-migration`.
- **Tests:** Added 6 new tests (4 stale-gate in `WorkspaceExecutionGateTests`, 1 early-kill classification in `TestRunFailureEnvelopeTests`, 1 metadata-drift in `SurfaceCatalogTests`). Test count moves from 485 → 491 with no regressions.
- **Docs:** `ai_docs/runtime.md` env-var table adds `ROSLYNMCP_ON_STALE` and `ROSLYNMCP_FAST_FAIL_FILE_LOCK`. `ai_docs/backlog.md` synced per the workflow contract.

## [1.16.0] - 2026-04-14

Top-10 backlog remediation pass v3 plus the **25-tool experimental → stable promotion batch** that lifts stable surface from 77 → 102 tools.

### Changed (experimental → stable promotion batch)

- **25 experimental tools promoted to stable** based on `ai_docs/audit-reports/20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §12 evidence (round-trip OK, schema accurate, p50 within budget). Catalog tier flips in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`. **No behavior change** — these tools have been stable for several releases and the promotion only updates `supportTier` discovery metadata. Promoted tools by category:
  - **`refactoring`:** `format_range_preview`, `extract_method_preview`, `extract_type_preview`, `move_type_to_file_preview`, `bulk_replace_type_preview`
  - **`file-operations`:** `create_file_preview`, `delete_file_preview`, `move_file_preview`
  - **`project-mutation`:** `add_package_reference_preview`, `remove_package_reference_preview`, `add_project_reference_preview`, `remove_project_reference_preview`, `set_project_property_preview`, `set_conditional_property_preview`, `add_target_framework_preview`, `remove_target_framework_preview`, `remove_central_package_version_preview`, `get_msbuild_properties`
  - **`scaffolding`:** `scaffold_test_preview`
  - **`editing`:** `apply_text_edit`, `add_pragma_suppression`
  - **`configuration`:** `set_editorconfig_option`, `set_diagnostic_severity`
  - **`undo`:** `revert_last_apply`

### Added

- **`format_check` (new experimental tool):** Solution-wide format verification. Iterates documents in the workspace (optionally filtered by `projectName`), runs `Formatter.FormatAsync` in-memory per document, compares via `SourceText.ContentEquals`, and returns `{ checkedDocuments, violationCount, violations: [{ filePath, lineCount }], elapsedMs }`. Does NOT apply changes — pair with `format_document_apply` to fix violations. Closes backlog `format-verify-solution-wide`.
- **`docs/stdio-client-integration.md` (new):** One-page custom stdio client integration guide covering NDJSON framing, init handshake, parameter naming, and minimal Python + C# client examples (~30 LOC each). Linked from `README.md` Getting Started and cross-referenced from `ai_docs/runtime.md`. Closes backlog `mcp-stdio-protocol-onboarding-docs`.

### Fixed

- **`schema-drift-jellyfin-audit` (split + fix):** Bundled audit row split into per-sub-item fixes:
  - **`SymbolResolver.ResolveByMetadataNameAsync` member fallback:** Fully-qualified names like `SampleLib.AnimalService.GetAllAnimals` now resolve to the **member** when the type lookup fails. The resolver splits on the last dot and falls back to `containingType.GetMembers(memberName).FirstOrDefault()`. Previously every member-level metadata-name lookup returned null, breaking `find_references` / `find_implementations` callers that pass member names. Regression coverage in `SymbolResolverRenameCaretTests` (3 new tests for member, type, and unknown).
  - **`set_diagnostic_severity` description:** `filePath` now prefixed with `(required)` so MCP clients render the requirement clearly.
  - **`apply_text_edit` description:** Includes a JSON example of the `edits` array shape for clarity.
  - Cleaned drift in prose docs that referenced phantom `RiskLevel` / `ChangeRisk` fields on `ImpactAnalysisDto`.
- **`symbol-search-payload-meta`:** Five tools that previously serialized bare `IReadOnlyList<>` arrays as the JSON root now wrap responses in `{ count, <name> }` envelopes so `_meta` injection (which requires a JSON object root in `ToolErrorHandler.InjectMetaIfPossible`) works. Affected tools: `symbol_search`, `go_to_definition`, `document_symbols`, `find_base_members`, `goto_type_definition`. **Breaking** — clients parsing these responses as bare arrays must now read `.symbols` / `.locations` / `.symbols` / `.members` / `.locations` respectively.
- **`claude-plugin-marketplace-version`:** `eng/update-claude-plugin.ps1` now reads the plugin version from the marketplace clone's `plugin.json` (not from Claude Code's pinned `installed_plugins.json`), updates `installed_plugins.json` to point at the current `installPath`, and prunes stale per-version cache directories so `~/.claude/plugins/cache/...` no longer accumulates dangling old plugin folders.

### Changed

- **`get_complexity_metrics` `filePaths` parameter (`roslyn-mcp-complexity-subset-rerun`):** New `filePaths: IReadOnlyList<string>?` parameter on `ICodeMetricsService.GetComplexityMetricsAsync` and the `get_complexity_metrics` tool. When supplied, results are filtered to methods declared in **any** of the given file paths (union semantics with the existing `filePath` single-file filter). Empty list and `null` both mean "no filter." Closes backlog `roslyn-mcp-complexity-subset-rerun`.
- **`test_related` description:** Clarifies that empty results mean no test name contains the source symbol's name as a substring (heuristic limitation), not that no related tests exist. Closes backlog `test-related-empty-docs`.

### Maintenance

- **Backlog:** Closed 10 rows in this pass (`schema-drift-jellyfin-audit`, `symbol-search-payload-meta`, `cohesion-metrics-null-lcom4`, `test-discover-pagination-ux`, `claude-plugin-marketplace-version`, `roslyn-mcp-complexity-subset-rerun`, `format-verify-solution-wide`, `test-related-empty-docs`, `mcp-stdio-protocol-onboarding-docs`, `experimental-promotion-batch-2026-04`). `cohesion-metrics-null-lcom4` and `test-discover-pagination-ux` closed as "verified already shipped / not reproducible" rather than new code.
- **Tests:** Added regression coverage for the v3 items: `SymbolResolverRenameCaretTests` (+3 metadata-name fallback tests), `Top10V3RegressionTests` (+4 tests covering items 2/6/7), `FormatVerifyTests`. Test count moves from 478 → 485 with no regressions.
- **Docs:** `ai_docs/runtime.md`, `README.md`, `ai_docs/prompts/deep-review-and-refactor.md`, and `ai_docs/prompts/experimental-promotion-exercise.md` updated to reflect new tier counts (102 stable / 29 experimental tools, 9 stable + 1 experimental resources).
- **Plan:** Implementation plan archived under `ai_docs/reports/20260414T171024Z_top10-remediation-plan-v3.md`.

## [1.15.0] - 2026-04-15

Two top-10 backlog remediation passes plus the post-1.14 cleanup batch.

### Fixed (P2 unblockers from remediation pass 1)

- **`unresolved-analyzer-reference-crash`:** `WorkspaceManager.LoadIntoSessionAsync` now strips `UnresolvedAnalyzerReference` entries from every project at load time via `Solution.RemoveAnalyzerReference` + `Workspace.TryApplyChanges`, surfacing a `WORKSPACE_UNRESOLVED_ANALYZER` warning through `workspace_status` so callers can still see what was filtered. Removes the per-service FLAG-A guards in `CompilationCache` and `FixAllService` now that the strip is centralized. Unblocks `find_unused_symbols`, `type_hierarchy`, `find_implementations`, `member_hierarchy`, `impact_analysis`, and `suggest_refactorings` on repos with netstandard2.0 analyzer references (e.g. Jellyfin).
- **`extract-method-apply-var-redeclaration`:** `ExtractMethodService` now consults `dataFlow.VariablesDeclared`. When the single flowsOut variable is declared OUTSIDE the extracted region the call site emits a plain assignment (`x = M(...)`) instead of `var x = M(...)`, eliminating CS0136 + CS0841 on apply. Compound writes (DataFlowsOut without AlwaysAssigned) are now rejected with a clear error.

### Fixed (P3 from remediation pass 1)

- **`get-source-text-line-range-ignored`:** `get_source_text` gains optional `startLine` / `endLine` / `maxChars` parameters with full validation; response envelope now includes `totalLineCount`, `requestedStartLine/EndLine`, `returnedStartLine/EndLine`, and `truncated` so callers can verify the slice. Pre-fix the parameters were silently dropped by the MCP framework.
- **`format-range-preview-nonfunctional`:** Added upfront parameter validation, end-position clamping, and disambiguated the `Formatter.FormatAsync` overload with `IEnumerable<TextSpan>` so the tool produces actual previews instead of erroring on every input.
- **`goto-type-definition-local-vars`:** `SymbolNavigationService` now walks constructed-generic type arguments, array element types, and pointer pointed-at types so navigating from a local of `IEnumerable<UserDto>` lands on `UserDto`'s source instead of returning empty.
- **`diagnostics-resource-timeout`:** `roslyn://workspace/{id}/diagnostics` enforces a 500-row cap and a Warning-severity floor by default; envelope adds `hasMore`, `cap`, `severityFloor`, `paginationNote`. Resource no longer hangs on multi-thousand-diagnostic solutions.
- **`project-diagnostics-large-solution-perf`:** `DiagnosticService` adds a per-(workspaceVersion, filters) result cache (LRU-ish, 8 entries per workspace, invalidates on `WorkspaceClosed`); analyzer pass is skipped when `severity=Error` and no loaded descriptor defaults to Error.
- **`code-fix-providers-missing-ca`:** Extracted `CodeFixProviderRegistry` that loads providers from `Microsoft.CodeAnalysis.CSharp.Features` and per-project analyzer references; `RefactoringService.PreviewCodeFixAsync` now dispatches via the registry, with the CS8019 fast path retained as fallback. CA*/IDE* fixes that previously errored now resolve when the analyzer assemblies are in the workspace.

### Fixed (P4 from remediation pass 1)

- **`apply-project-mutation-whitespace`:** `ProjectMutationService.RemoveElementCleanly` drops one whitespace neighbour (preferring leading text) and prunes empty parents so an add → remove round-trip no longer leaves a blank line or empty `<ItemGroup />`.
- **`analyze-data-flow-inverted-range`:** `FlowAnalysisService.ResolveAnalysisRegionAsync` rejects `startLine > endLine`, `startLine < 1`, `endLine < 1` upfront with a structured `ArgumentException` instead of falling through to "No statements found in the line range N-M".

### Fixed (post-1.14 cleanup batch)

- **Response key casing:** Added `PropertyNamingPolicy = CamelCase` to `JsonDefaults.Indented` so all tool and resource responses use camelCase keys, matching input parameter casing. **Breaking:** external clients parsing PascalCase keys (e.g. `PreviewToken`, `WorkspaceId`, `ErrorCount`) must update to camelCase (`previewToken`, `workspaceId`, `errorCount`).
- **`server_info` version comparison:** `NuGetVersionChecker` now compares NuGet versions semantically (`Version.TryParse`) instead of taking the last array element; prevents stale NuGet CDN data from reporting an older version as "latest" with `updateAvailable: true`.
- **Stdio flush on exit:** Added `Console.Out.Flush()` in the `ApplicationStopping` handler and after `host.RunAsync()` to prevent buffered MCP JSON responses from being lost on process exit.
- **`add_central_package_version_preview` / `remove_central_package_version_preview`:** Throws `FileNotFoundException` with an actionable message (searched directory path + CPM setup instructions) when `Directory.Packages.props` is missing.
- **`fix_all_preview` IDE diagnostics:** Added `GetAlternativeToolHint()` mapping for common IDE diagnostics (IDE0005, IDE0007/8, IDE0055, IDE0160/1, IDE0290) that lack FixAll providers, directing agents to the correct alternative tool. Startup logging now reports skipped provider count.

### Performance (remediation pass 2)

- **`scaffold-type-apply-perf`:** `RefactoringService.PersistDocumentSetChangesAsync` now attempts `Workspace.TryApplyChanges` first and only falls back to `ReloadAsync` when MSBuildWorkspace rejects the change. Eliminates the unconditional ~10 s reload on every `scaffold_type_apply` and `create_file_apply`.
- **`di-registrations-scan-caching`:** `DiRegistrationService` adds a per-(workspaceVersion, projectFilter) result cache (LRU-ish, 8 entries per workspace, invalidates on `WorkspaceClosed`). Repeat callers no longer pay the ~12 s solution scan twice.
- **`nuget-vuln-scan-caching`:** `NuGetDependencyService.ScanNuGetVulnerabilitiesAsync` caches results by `(workspaceVersion, projectFilter, includeTransitive, lockfileHash)`. Lockfile hash is computed from `Directory.Packages.props` and per-project `packages.lock.json` so external edits invalidate cleanly without a workspace bump. Repeat scans drop from ~11 s to <50 ms.
- **`solution-project-index-by-name`:** New `IWorkspaceManager.GetProject(workspaceId, projectNameOrPath)` API backed by a per-`workspaceVersion` `Dictionary<string, Project>` index. Single source of truth for project name / file path lookups across services.
- **`prompt-timeout-explain-refactor`:** Both `explain_error` and `refactor_and_validate` prompts now run the diagnostics step at `severityFilter="Warning"` so the analyzer-skip short-circuit fires; both wrap their bodies in a 20 s linked `CancellationTokenSource` that returns a clear "aborted at the diagnostics step" message instead of hanging through the framework's 25 s default timeout.

### Added (remediation pass 2)

- **`apply_with_verify` (new tool):** Atomic apply → `compile_check` → revert primitive. Applies the preview, captures pre/post error fingerprints, and rolls back via `revert_last_apply` when new compile errors appear. Pass `rollbackOnError=false` to keep the broken state for inspection.
- **`remove_interface_member_preview` (new tool):** Composite preview that resolves an interface member, gathers every implementation via `SymbolFinder.FindImplementationsAsync`, refuses if any non-implementation caller exists, and produces a single `dead_code_preview` token spanning the interface declaration plus every implementation. Apply via `remove_dead_code_apply`.
- **`source_file_lines` resource template:** `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}` returns a 1-based inclusive line slice (mirrors the `get_source_text` tool's slicing). Response is prefixed with a `// roslyn://… lines N..M of T` comment marker.
- **Long-running tool progress:** `project_diagnostics`, `nuget_vulnerability_scan`, and `extract_and_wire_interface_preview` now accept `IProgress<ProgressNotificationValue>` and emit start/finish progress events so agents can distinguish "still working" from "actually hung".
- **Shared `SourceTextSlicer` helper:** Extracted from `WorkspaceTools.SliceLines` into `RoslynMcp.Roslyn.Helpers.SourceTextSlicer` so the tool and the new resource template use the same code path.

### Maintenance

- **Backlog:** Closed 19 rows across the two remediation passes (10 + 9 — `cohesion-metrics-null-lcom4` was demoted to P4 with a "needs fresh repro" note rather than closed). Added 2 follow-up rows from observations during the work: `solution-project-index-by-name` (subsequently closed in pass 2) and `source-file-resource-line-range-parity` (closed in pass 2). `apply-text-edit-diff-quality` closed via investigation pass — added 4 edge-case repro tests to `DiffGeneratorTests`; no concrete bug reproduces after the overlap-safety + syntax-preflight work shipped in earlier PRs.
- **`compile_check` restore hint:** `CompileCheckDto` includes `RestoreHint` field; heuristic detects 10+ CS0234 errors and suggests `dotnet restore` + `workspace_reload`.
- **`compile_check` partial results on cancellation:** `CompileCheckDto` adds `Cancelled`, `CompletedProjects`, and `TotalProjects` fields; cancellation returns diagnostics collected so far instead of throwing `OperationCanceledException`.
- **`project_diagnostics` summary mode:** New `summary` parameter (bool, default false); when true, returns per-diagnostic-ID counts grouped by severity instead of individual diagnostic rows (10-100x smaller payload).
- **`_meta` injection logging:** `ToolErrorHandler.InjectMetaIfPossible` emits `Trace.TraceWarning` when meta injection is skipped (null snapshot or non-object response root), aiding observability audits.
- **Tests:** Added regression coverage for every remediation item; full suite now 478 tests (up from 466) with no regressions. New test files: `UnresolvedAnalyzerReferenceTests`, `FormatRangeServiceTests`, `GoToTypeDefinitionTests`, `CodeFixProviderRegistryTests`, `Top10V2RegressionTests`. Plus extensions to existing test files for extract-method, project-mutation, flow-analysis, diagnostic-service, workspace-tools, workspace-resource, and diff-generator coverage.
- **Plans:** Implementation plans archived under `ai_docs/reports/20260414T220000Z_top10-remediation-plan.md` and `20260414T231900Z_top10-remediation-plan-v2.md`.

## [1.14.0] - 2026-04-14

### Changed

- **`project_diagnostics`:** Default minimum severity is **Info** (was Warning); default page size **`limit`** is **200** (was 50); added `paginationNote` when more diagnostics exist; removed `severityHint` from `DiagnosticsResultDto` (defaults now align with totals).
- **`find_references` / `find_implementations` / `find_overrides`:** Responses use a **`items`** array (`find_references` keeps pagination metadata); stable sort for implementations and overrides.
- **`find_references_bulk`:** Clearer tool/docs text for the `symbols` parameter and improved `BulkSymbolLocator` error messages (common mistakes + schema).
- **`workspace_status`:** Summary DTO adds **`isReady`**, **`restoreHint`**, **`solutionFileName`**; new **`workspace_health`** tool alias (same lean summary).
- **MSBuild / moves:** `project` parameters unified to **`projectName`** where applicable; **`targetFilePath`** for file moves; target-framework preview uses MSBuild evaluation when TF is only in imports.
- **Cross-project move:** Default namespace follows target project; file-scoped namespace trivia fixed after removing whole-file normalize.
- **Core / Roslyn boundary:** Moved workspace and compilation surface that depends on Roslyn into `RoslynMcp.Roslyn` — `IWorkspaceManager`, `ICompilationCache`, `IPreviewStore`, and `PreviewStore` now live under `RoslynMcp.Roslyn.Contracts` / Roslyn services. `RoslynMcp.Core` stays free of Roslyn workspace types so hosts reference the Roslyn assembly for those APIs.
- **Orchestration:** Replaced `IOrchestrationService` / `OrchestrationService` with focused orchestrators (`IPackageMigrationOrchestrator`, `IClassSplitOrchestrator`, `IExtractAndWireOrchestrator`, `ICompositeApplyOrchestrator`) plus shared `OrchestrationMsBuildXml`. `OrchestrationTools` and DI registrations use the new interfaces.
- **Prompts:** Split `RoslynPrompts` into partials (`RoslynPrompts.RefactoringWorkflows`, `.AnalysisWorkflows`, `.GuidedWorkflows`) and centralized prompt construction in `PromptMessageBuilder`.
- **DI:** Preview stores (`IPreviewStore`, `IProjectMutationPreviewStore`, `ICompositePreviewStore`) share one `ResolvePreviewStoreConfiguration` path in `ServiceCollectionExtensions`.
- **Workspace status:** `IWorkspaceManager` exposes `GetStatusAsync`; MCP workspace status resources and call sites that need load-lock semantics use the async path.

### Removed

- **`IOrchestrationService` / `OrchestrationService`** (superseded by the orchestrator split above).

### Added

- **Tests:** `SymbolMapperTests`, `FixAllServiceIntegrationTests`, and `SuppressionServiceTests`; test host/container updates for the new orchestrator and workspace contracts.

### Fixed

- **Tests / resources:** Workspace resource and observability tests updated for async workspace status; undo/edit integration tests aligned with moved preview/workspace types.

### Maintenance

- **`eng/sync-deep-review-backlog.ps1`:** Merges deep-review candidates into the agentic backlog layout (P2/P3/P4 tables with `pri | blocker | deps | do`); splices open-work sections before `## Evidence and paths` or `## Refs`.
- **`ai_docs/backlog.md`:** Reorganized for agent-first use (thematic index, dependency edges, split P3/P4 tables, evidence section); closed Top-10 backlog remediation items (text-edit safety, rename caret, move-type namespace, noisy diffs, MSBuild param alignment, centralized TF, diagnostics defaults, find-overrides, bulk refs schema, workspace readiness).

## [1.13.0] - 2026-04-13

### Fixed

- **`extract_interface_preview`:** No longer adds duplicate base type (CS0528) when the source type already implements the named interface.
- **`extract_and_wire_interface_preview`:** Generated interface files no longer include implementation-only usings (DiffPlex, Microsoft.Extensions, etc.); tightened non-System namespace filter to fully-qualified name match.
- **`scaffold_type_preview`:** Rejects invalid C# identifiers (e.g. `2InvalidName`) via `IdentifierValidation` — previously generated uncompilable code.
- **`scaffold_test_preview`:** Validates that the target project is a test project (`IsTestProject` or test framework packages); previously accepted non-test projects and generated non-compiling MSTest code.
- **`add_central_package_version_preview`:** XML formatting fixed — `<PackageVersion>` elements now use `AddChildElementPreservingIndentation` instead of raw `Add()` that jammed elements against `</ItemGroup>`.
- **`revert_last_apply`:** Preview tokens created before a reverted operation now remain valid. `UndoSnapshot` captures the pre-apply workspace version and `RestoreVersion` API restores it after revert, fixing the preview-store coupling bug.
- **`ClientRootPathValidator.ResolvePath`:** Fixed crash when parent-directory symlink walk reached a drive root (e.g. `C:\`).
- **`test_coverage`:** Returns structured `FailureEnvelope` (with `ErrorKind: "CoverletMissing"` or `"TestFailure"`) instead of `Success: true` with error text in body when coverage data is unavailable.
- **`ToolErrorHandler`:** MCP SDK parameter binding failures (inner `JsonException`/`ArgumentException`) now surface schema-shaped hints instead of generic "error invoking" text.

### Changed

- **Complexity refactoring:** Extracted helpers from three worst complexity hotspots:
  - `DiagnosticService.GetDiagnosticsAsync` (CC=25) → `CollectProjectDiagnosticsAsync` + `CollectDiagnostics`
  - `ScriptingService.EvaluateAsync` (CC=20) → `TryAcquireCapacityAsync`
  - `SymbolRelationshipService.GetCallersCalleesAsync` (CC=20) → `CollectCallersAsync` + `CollectCalleesAsync`
- **`CompileCheckService.CheckAsync`:** 8-parameter signature replaced with `CompileCheckOptions` record. MCP tool parameters unchanged.
- **`project_diagnostics`:** New `diagnosticId` filter parameter for server-side filtering by diagnostic ID (e.g. `CS8019`, `CA1000`).

### Added

- **Security analyzers:** `Microsoft.CodeAnalysis.NetAnalyzers` (10.0.100) and `Microsoft.CodeAnalysis.BannedApiAnalyzers` (4.14.0) added to `Directory.Build.props` with `BannedSymbols.txt` banning `BinaryFormatter` and shell-execute `Process.Start`.
- **`IWorkspaceManager.RestoreVersion`:** New API for restoring workspace version counter after undo/revert operations.
- **Tests:** 48 new tests — 8 `ClientRootPathValidator` tests (ResolvePath + ValidatePath), 12 `IdentifierValidation`, 7 `DiffGenerator`, 21 `ParameterValidation`.

## [1.12.1] - 2026-04-13

### Fixed

- **`server_info`:** `update.updateAvailable` now uses proper semver comparison (`Version.TryParse`) instead of string inequality, so locally-built versions ahead of NuGet no longer falsely report an update is available.

### Changed

- **CI: publish-nuget workflow:** Added version tag push trigger (`v[0-9]+.[0-9]+.[0-9]+`) so pushing a `v*` tag automatically publishes to NuGet.org without requiring a GitHub Release.

## [1.12.0] - 2026-04-13

### Fixed

- **`fix_all_preview` / `fix_all_apply`:** Non-IDE diagnostic IDs (e.g. `SCS*`, `MA*`, other third-party prefixes) now run the same project analyzer pipeline as `CA*` when collecting occurrences, instead of falling back to compiler-only diagnostics (which never report analyzer rules).
- **`roslyn://workspace/{workspaceId}/file/{filePath}` (`source_file`):** Decode percent-encoded paths, normalize `/` to the platform directory separator, and reject non-absolute paths with a clear error before document lookup.

### Changed

- **Stable promotions (7 tools):** `get_syntax_tree`, `workspace_changes`, `suggest_refactorings`, `get_operations`, `get_editorconfig_options`, `evaluate_msbuild_property`, `evaluate_msbuild_items` promoted from experimental to stable based on multi-repo deep-review audit evidence.
- Deep-review audit intake: 3 new audit reports, 2 rollups, refreshed backlog (34 open items deduplicated and reprioritized), updated eng scripts and deep-review prompt.

### Added

- **`WorkspaceResources`** handler: dedicated MCP resource class for workspace-scoped resource endpoints, including workspace list/verbose, status/verbose, projects, diagnostics, and source file resources.
- 3 new `WorkspaceResourceTests` covering URI-encoded paths, forward-slash normalization, and relative-path rejection for the `source_file` resource.

## [1.11.2] - 2026-04-13

### Fixed

- **Resource leak:** `UndoService` and `ChangeTracker` now subscribe to `IWorkspaceManager.WorkspaceClosed` and clear per-workspace state when workspaces close, following the `CompilationCache` pattern. Previously, undo snapshots and change records accumulated without cleanup.
- **Dead code:** removed unused `MsBuildMetadataHelper.FindDirectoryBuildProps` (zero references) and `CorrelationContext.BeginScope()` + `CorrelationScope` class (zero call sites).

### Added

- **MCP prompts (3):** `guided_extract_method`, `msbuild_inspection`, `session_undo` (experimental), wired in `RoslynPrompts` and `ServerSurfaceCatalog`.
- **Plugin skills (5):** `code-actions`, `test-triage`, `snippet-eval`, `project-inspection`, `session-undo`.
- **Backlog:** 13 new findings from parallel review/dead-code/test-coverage/security audits added to `ai_docs/backlog.md` (21 total items, deduplicated and reprioritized).

### Changed

- **Plugin skills:** `user-invocable` / `argument-hint` on skills where helpful; **Server discovery** sections (`server_info`, `server_catalog`, `discover_capabilities`, related MCP prompts); expanded **`refactor`** with extract-method, code actions, fix-all, orchestration, and format-range rows; **`update`** documents `just tool-update` / `just tool-install-local`; **`publish-preflight`** documents `just` recipes; **`test-coverage`** uses `document_symbols` (replaces nonexistent `get_symbol_outline`).
- **`guided_extract_interface` prompt:** apply step now references `extract_interface_apply` (not `rename_apply`).
- **`server_info` tool description:** no longer hardcodes experimental prompt count (use live `surface.prompts`).

## [1.11.1] - 2026-04-13

### Changed

- **Refactored `ExtractMethodService.PreviewExtractMethodAsync`** — extracted 5 private helper methods (`ResolveDocumentAsync`, `FindEnclosingMethodAndStatements`, `AnalyzeFlowAndInferSignature`, `BuildMethodAndCallSite`, `ReplaceStatementsAndInsertMethod`) from the monolithic method (CC=31, MI=21.7). Orchestrator method now delegates to focused helpers. No behavior change.

### Removed

- **Dead code cleanup** — removed unused `ActiveEvaluations` and `AbandonedEvaluations` internal properties from `ScriptingService` (0 references confirmed via `find_references`). Backing fields retained — used by `EvaluateAsync` internals.

## [1.11.0] - 2026-04-13

### Added

- **`suggest_refactorings`** — read-only tool combining complexity metrics, LCOM4 cohesion analysis, and unused symbol detection into ranked refactoring suggestions with recommended tool sequences. Each suggestion includes severity (high/medium/low), category, target symbol location, and the tools to use.
- **Stable promotions (3 tools):** `get_code_actions`, `preview_code_action`, `apply_code_action` promoted from experimental to stable. Selection-range refactorings (introduce parameter, inline temporary) verified working in v1.10.0.
- **Change tracker coverage expansion:** `ProjectMutationService.ApplyProjectMutationAsync` and `CompositeApplyOrchestrator.ApplyCompositeAsync` now record changes via `IChangeTracker`, closing the blind spot for project mutations and composite operations.
- 128 tools (69 stable / 59 experimental).

## [1.10.0] - 2026-04-12

### Added

- **`extract_method_preview` / `extract_method_apply`** — custom extract-method refactoring using Roslyn's `DataFlowAnalysis` for parameter inference and `ControlFlowAnalysis` for single-exit validation. Supports void and single-return-value extraction with automatic call-site generation.
- **`workspace_changes`** — read-only tool listing all mutations applied to a workspace during the session, with descriptions, affected files, tool names, and timestamps. Integrated into `RefactoringService` and `EditService` apply paths.
- **`/roslyn-mcp:extract-method` plugin skill** — guided extract-method workflow via the Claude Code plugin (11 skills total).
- **Selection-range code action discovery** — verified that `get_code_actions` with `endLine`/`endColumn` surfaces introduce-parameter and inline-temporary-variable refactorings from the Roslyn Features assembly. Updated tool description and added "Selection-Range Refactoring" workflow hint.
- **`RefactoringProbe.cs`** sample fixture for selection-range and extract-method tests.
- 126 tools total (66 stable / 60 experimental).

## [1.9.0] - 2026-04-11

### Added

- **Stable promotions (4 tools):** `semantic_search`, `analyze_data_flow`, `analyze_control_flow`, `evaluate_csharp` promoted from experimental to stable. Catalog `2026.04` now ships 66 stable / 57 experimental tools (123 total).
  - `semantic_search` — verbose-query fallback (#110) and exact-match implementing predicate (#123) shipped; backlog cleared.
  - `analyze_data_flow` / `analyze_control_flow` — expression-bodied member support added; read-only, well-tested.
  - `evaluate_csharp` — timeout budget, infinite-loop safety, abandoned-cap, and outer-cancellation all exercised in integration tests.

### Fixed

- **`apply_text_edit` line-break preservation** (`dr-apply-text-edit-line-break-corruption`). When an edit span ends at column 1 of a line (swallowing the line break), and the replacement text does not end with a newline, the original line ending is now appended to prevent line collapse at method/declaration boundaries. (#121)
- **`diagnostic_details` curated fix contract** (`dr-code-fix-preview-vs-diagnostic-details-curated-gap`). Removed false curated fix promises for CS0414, CS8600/8602/8603, CA2234, CA1852 from `GetSupportedFixes` — `code_fix_preview` only supports CS8019/IDE0005. Added `GuidanceMessage` field to `DiagnosticDetailsDto` directing users to `get_code_actions` + `preview_code_action`. (#122)
- **`semantic_search` implementing predicate accuracy** (`dr-semantic-search-idisposable-predicate-accuracy`). Replaced `Contains()` with `Equals()` on `ToDisplayString()` in `AddImplementingPredicate` to prevent false positives where "IDisposable" matched "IAsyncDisposable" via substring. (#123)
- **`find_shared_members` partial-class crash** (`dr-find-shared-members-locator-invalidargument`). Built a semantic model map for all partial declarations so `MethodAccessesMember` uses the correct model for each method's syntax tree, preventing "Syntax node is not within syntax tree" exceptions. (#124)
- **`SecurityDiagnosticIntegrationTests` fixture** (`dr-security-integration-insecurelib-not-in-sln`). Added `EnableNETAnalyzers=true` and `AnalysisLevel=latest-all` to InsecureLib.csproj so CA5350/CA5351 fire in MSBuildWorkspace. Fixes 4 previously failing tests. (#125)
- **`project_diagnostics` empty first page hint** (`dr-project-diagnostics-info-only-empty-first-page`). Added `SeverityHint` field when returned arrays are empty but lower-severity diagnostics exist. (#126)
- **`get_editorconfig_options` completeness** (`dr-get-editorconfig-options-incomplete-after-set`). Supplemented Roslyn-enumerated keys with on-disk keys from `.editorconfig` after `set_editorconfig_option` writes. (#126)
- **`get_code_actions` parameter validation** (`dr-get-code-actions-opaque-error-on-bad-contract`). Validates `startLine`/`startColumn` >= 1 with a helpful error when callers pass wrong parameter names. (#126)

### Changed

- **`project_diagnostics` tool description** updated with response JSON field names (`totalErrors`, `totalWarnings`, `totalInfo`, `compilerErrors`, `analyzerErrors`, `workspaceErrors`, `severityHint`) and default severity documentation. (#126)
- **`find_references_bulk` tool description** now includes a concrete JSON parameter shape example to prevent first-invocation failures. (#127)

## [1.8.2] - 2026-04-09

### Fixed

- **Structured `test_run` failure envelope** (`test-run-failure-envelope`). When `dotnet test` exits without TRX output (MSB3027/3021 file locks, build failures, timeouts), the result now carries a `TestRunFailureEnvelopeDto` with `ErrorKind` (FileLock/BuildFailure/Timeout/Unknown), `IsRetryable`, `Summary`, and `StdOutTail`/`StdErrTail` instead of throwing a bare invocation error.
- **`apply_text_edit` range validation** (`apply-text-edit-invalid-edit-corrupt-diff`). Malformed `TextEditDto` values (null `NewText`, non-positive line/column, out-of-bounds, reversed ranges) are now rejected with a structured `ArgumentException` before any disk write or diff generation.
- **`revert_last_apply` disk consistency** (`revert-last-apply-disk-consistency`). `IUndoService` now accepts explicit `FileSnapshotDto` pairs; `RevertAsync` restores disk directly from these authoritative snapshots instead of relying on the fragile `Solution.GetChanges` path. Legacy solution-based path gains a disk-walk safety net.
- **`set_editorconfig_option` undo retrofit** (`set-editorconfig-option-not-undoable`). Now participates in the undo stack via the file-snapshot path; `revert_last_apply` restores pre-write content or deletes the file if the set call created it. Tool description updated.
- **`semantic_search` verbose-query fallback** (`semantic-search-zero-results-verbose-query`). Long natural-language queries that fail structured parsing decompose into stopword-filtered tokens; the token-OR fallback matches symbols whose names contain any token. New `SemanticSearchDebugDto` on the response shows parsed tokens, applied predicates, and fallback strategy.
- **`workspace_load` idempotent by path** (`workspace-session-deduplication`). Repeat loads of the same solution path return the existing `WorkspaceId` instead of creating a duplicate. Includes a race-window check for concurrent callers.
- **`find_overrides` auto-promotes to virtual root** (`find-overrides-virtual-declaration-site-doc`). Invoking at an override site now walks back to the original virtual/interface declaration before the search, producing the same complete result set as invoking at the virtual declaration.
- **`find_type_usages` cref classification** (`find-type-usages-cref-classification`). New `TypeUsageClassification.Documentation` value; `<see cref="X"/>` doc-comment references are now classified as `Documentation` instead of `Other`.
- **`find_references_bulk` schema error UX** (`find-references-bulk-schema-error-ux`). Invalid `BulkSymbolLocator` shapes now include an inline JSON example in the error message.
- **MSBuild tools bad-argument message** (`msbuild-tools-bad-argument-message`). `ResolveRoslynProject` now lists loaded project names in the error when the caller passes an unknown project.
- **`move_file_preview` truncation marker** (`move-file-preview-large-diff-truncation`). The FLAG-6A truncation marker now includes the paths of omitted files.
- **`analyze_snippet` CS0029 span** (`analyze-snippet-cs0029-literal-span`). Regression test confirms the diagnostic span covers the string literal in user coordinates.

### Changed

- Tool description clarifications for `compile_check`, `project_diagnostics`, `workspace_load`, `evaluate_msbuild_items`, `evaluate_msbuild_property`, `add_package_reference_preview`, `semantic_search`, `symbol_search`, `server_info`, `set_editorconfig_option`, `find_overrides`, `test_run`.
- `eng/verify-release.ps1` now passes `--logger "console;verbosity=normal"` so failing test names surface through wrapper scripts.
- `eng/verify-version-drift.ps1` — new automated version-string drift check across all 5 version files; wired into `verify-release.ps1`.
- Deep-review audit intake: 3 raw audit reports, 1 rollup, updated procedures and eng scripts.

## [1.8.1] - 2026-04-08

### Changed

- **Package release under the `Darylmcd.RoslynMcp` NuGet id.** Version bump from `1.8.0` to `1.8.1` so the first published artifact carries the renamed id from a clean version slot. No source/feature changes vs `1.8.0` — the bump exists purely to give the renamed package a fresh version slot.
- **Package consumer README.** The NuGet package now ships a focused `src/RoslynMcp.Host.Stdio/README.md` aimed at consumers (install + use + MCP client config + security/privacy + configuration env vars + cross-platform notes). The previous behavior was to pack the repo root `README.md`, which is a much longer contributor-facing document. The repo root README stays unchanged — only the `<PackageReadmeFile>` source path changed.

## [1.8.0] - 2026-04-08

### Changed

- **Catalog `2026.04` — promote six read-only advanced-analysis tools to stable** (`find_unused_symbols`, `get_di_registrations`, `get_complexity_metrics`, `find_reflection_usages`, `get_namespace_dependencies`, `get_nuget_dependencies`). Evidence: 2026-04-08 multi-repo deep-review raw audits and rollup in `ai_docs/reports/`. `semantic_search` remains **experimental** (open ranking/UX backlog items). Updated `docs/product-contract.md` and `docs/experimental-promotion-analysis.md` accordingly.
- **NuGet package id renamed `RoslynMcp` → `Darylmcd.RoslynMcp`.** The unprefixed `RoslynMcp` id was published by another author (`chrismo80`, v1.1.1) on 2026-04-08 shortly before our first publish attempt, so it is permanently unavailable. The CLI command name remains `roslynmcp` — `.mcp.json`, plugin manifests, and any user config that references the binary on `PATH` are unaffected. Install command becomes `dotnet tool install -g Darylmcd.RoslynMcp`. Existing local installs of the bare `RoslynMcp` package should be uninstalled (`dotnet tool uninstall -g RoslynMcp`) before installing the new id; the local-dev `PackAndReinstallGlobalTool` MSBuild target now uninstalls both ids before reinstalling.

## [1.7.0] - 2026-04-08

### Fixed

- **P1 / `rename_preview` accepts illegal C# identifiers** (`rename-preview-validate-identifier`). Calling `rename_preview newName="2foo"` (numeric prefix), `"class"` (reserved keyword), `"var"` (contextual keyword), or `""` (empty) previously produced a multi-file preview that, if applied, would break compilation across the entire solution. `PreviewRenameAsync` now calls a new `IdentifierValidation.ThrowIfInvalidIdentifier` helper that validates the new name against `SyntaxFacts.IsValidIdentifier`, rejects reserved and contextual C# keywords, and accepts verbatim identifiers (`@class`) by stripping the `@` and skipping the keyword guards. Throws `InvalidOperationException` with a descriptive message before any cross-file changes are computed.
- **`rename_preview` no-op silently returns empty Changes** (`rename-preview-noop-warning`). Renaming a symbol to its current name now returns `Warnings: ["New name '...' matches the existing name; no changes were produced."]` so callers can distinguish "rename succeeded with no actual references" from "no-op rename". Comparison is case-sensitive (`Ordinal`) so `Foo → foo` is still treated as a real rename.
- **`organize_usings_preview` and `move_type_to_file_preview` leave stray blank lines** (`organize-usings-and-move-type-trivia`). Both tools shared the same trivia bug: `organize_usings_preview` used `SyntaxRemoveOptions.KeepExteriorTrivia` which preserved the trailing newline of each removed `using`, leaving a blank line behind; `move_type_to_file_preview` used `NormalizeWhitespace()` which inserted artificial blank lines before the namespace. Both paths now route through a new shared `TriviaNormalizationHelper` (`NormalizeLeadingTrivia`, `NormalizeUsingToMemberSeparator`, `CollapseBlankLinesInUsingBlock`) that canonicalizes leading/trailing trivia after deletes and moves while preserving XML doc comments and other non-whitespace trivia. Same fix also applied to `TypeMoveService.RemoveUnusedUsingsAsync`.

### Added

- **`apply_text_edit` and `apply_multi_file_edit` are now revertible** (`apply-text-edit-undo-stack`). Both tools were previously documented as "DISK-DIRECT, NOT REVERTIBLE" — they did not enter the undo stack so `revert_last_apply` could not undo them. `EditService` now takes an optional `IUndoService` and captures a single pre-apply `Solution` snapshot before mutating. A new `IEditService.ApplyMultiFileTextEditsAsync` method captures **one** snapshot at the batch boundary so a multi-file batch is reverted atomically. Tool descriptions for `apply_text_edit`, `apply_multi_file_edit`, and `revert_last_apply` updated to reflect the new contract; only file create/delete/move and project file mutations remain unrevertible.
- **`find_unused_symbols` is now convention-aware by default** (`find-unused-symbols-convention-aware`). New `excludeConventionInvoked` parameter (default `true`) skips symbols recognized as convention-invoked: EF Core `*ModelSnapshot`, xUnit/NUnit/MSTest fixtures, ASP.NET middleware (`Invoke`/`InvokeAsync(HttpContext)` shape), SignalR `Hub` subclasses, FluentValidation `AbstractValidator<T>` subclasses, and Razor `PageModel` subclasses. Detection is name-shape based via two new private helpers `IsConventionInvokedType` (walks the base-type chain by simple name plus method-shape detection) and `IsConventionInvokedMember` (delegates to type detection plus `[DbContext]`/`[Migration]` attribute checks). Set `excludeConventionInvoked=false` to opt out and see raw counts. No NuGet dependencies pulled — detection works against stub base types in user fixtures.
- New shared helper `src/RoslynMcp.Roslyn/Helpers/IdentifierValidation.cs` for the rename validation path.
- New shared helper `src/RoslynMcp.Roslyn/Helpers/TriviaNormalizationHelper.cs` for the trivia/blank-line fixes.
- New sample fixture `samples/SampleSolution/SampleLib/ConventionFixtures.cs` with stub base types and 5 convention-shaped sample classes (`SampleValidator`, `ChatHub`, `IndexModel`, `MyDbContextModelSnapshot`, `LoggingMiddleware`) plus a control type that should still be reported.
- New test class `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs` with 4 tests covering the text-edit undo path including single-slot snapshot semantics across rename → text edit → revert.
- 6 new rename-validation tests in `RefactoringToolsIntegrationTests.cs`, 3 new convention-aware tests in `DeadCodeIntegrationTests.cs`, 3 new trivia tests across `RefactoringToolsIntegrationTests.cs` and `TypeMoveTests.cs`. Total new test count: 16. Suite is now 267/267 (was 251/251).

### Refactored

- **`ScriptingService.EvaluateAsync` complexity reduction.** Cyclomatic complexity 36 → 20, LOC 282 → 204, MaintainabilityIndex 19.5 → 26.3. Extracted `MarkAbandonedIfWorkerStillRunning`, `LogHardDeadlineCritical`, `BuildHardDeadlineDto`, and `BuildOutcomeDto` so the FLAG-5C concurrency race steps (cancel → deadline → outcome) are no longer buried under DTO boilerplate. Behavior preserved — verified by all 8 ScriptingService characterization tests including the abandoned-cap and outer-cancel paths.
- **`MutationAnalysisService.ClassifyTypeUsageAfterWalk` complexity reduction.** Cyclomatic complexity 31 → 21, LOC 58 → 31, MaintainabilityIndex 40.1 → 49.7. Replaced an if-chain with a switch expression using type patterns and `when` guards.
- **`CodeMetricsService.VisitControlFlowNesting` complexity reduction.** Original 109-LOC method (cyclomatic 28) decomposed into a small `VisitChildForNesting` dispatcher plus per-shape helpers `VisitForLoop` and `VisitTryStatement`. Single-body statement cases grouped under a comment band so multi-body cases stay visually distinct.
- **`UnusedCodeAnalyzer.ShouldSkipSymbolForUnusedAnalysis` complexity reduction.** Cyclomatic complexity 24 → no longer in the hotspot list. Decomposed into 11 named predicate helpers (`IsTestFixtureFiltered`, `IsPublicFiltered`, `IsEnumMemberFiltered`, etc.) aggregated via `||`, making it trivial to add new convention exclusions in this release.

## [1.6.1] - 2026-04-07

### Added

- **Claude Code plugin**: packaged as a first-class Claude Code plugin installable via `/plugin install`. Includes plugin manifest (`.claude-plugin/plugin.json`), marketplace descriptor (`.claude-plugin/marketplace.json`), and user-configurable environment variables.
- **10 plugin skills**: `/roslyn-mcp:analyze` (solution health), `/roslyn-mcp:refactor` (guided refactoring), `/roslyn-mcp:review` (semantic code review), `/roslyn-mcp:document` (XML doc generation), `/roslyn-mcp:security` (security audit), `/roslyn-mcp:dead-code` (dead code cleanup), `/roslyn-mcp:test-coverage` (coverage analysis), `/roslyn-mcp:migrate-package` (NuGet migration), `/roslyn-mcp:explain-error` (diagnostic fixer), `/roslyn-mcp:complexity` (hotspot analysis).
- **Plugin safety hooks**: PreToolUse guard enforcing preview-before-apply pattern; PostToolUse reminder to compile-check after structural refactorings.
- `ValidationServiceOptions.VulnerabilityScanTimeout` (default 2 minutes) and `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` env var to make the NuGet vulnerability scan timeout configurable.
- **Cross-tool compilation cache** (`ICompilationCache`): per-workspace, version-keyed cache that shares warm `Compilation` instances across diagnostics, unused-code analysis, and dependency analysis — eliminating redundant Roslyn compilation passes.
- `eng/update-claude-plugin.ps1` utility to refresh the locally cached Claude Code plugin from the marketplace repository without opening the REPL.
- `REINSTALL.md` consolidated installation and reinstall reference.
- **Per-workspace reader/writer lock**: `IWorkspaceExecutionGate` exposes `RunReadAsync<T>` (concurrent reads against the same workspace), `RunWriteAsync<T>` (writes exclusive against all reads/writes on the same workspace), and `RunLoadGateAsync<T>` (the global load gate used by `workspace_load`/`reload`/`close`). Each workspace is gated by a `Nito.AsyncEx.AsyncReaderWriterLock` so reader fan-out is bounded only by the global throttle. There is no opt-out — this is the only lock model the gate ships.
- `Nito.AsyncEx 5.1.2` dependency on `RoslynMcp.Roslyn` (transitive into the host).
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs` — dedicated unit-test class for the gate covering read/read concurrency, read/write exclusion, write/write serialization, FIFO writer fairness, post-close `RunReadAsync` rejection, the workspace-close double-acquire race, and rate-limit / request-timeout / global-throttle enforcement.
- `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` — `[TestCategory("Benchmark")]` benchmark that 4 parallel reads against the same workspace complete in roughly 1× single-read wall time on the per-workspace RW lock.

### Performance

- **Parallelized hot paths:** `ReferenceService`, `UnusedCodeAnalyzer`, `DiagnosticService`, `MutationAnalysisService`, and `ConsumerAnalysisService` now fan out per-location and per-project work concurrently, bounded by processor-count-derived semaphores.
- **Per-workspace diagnostic cache** in `DiagnosticService` avoids redundant recompilation for consecutive diagnostic queries against the same workspace version.
- **Compilation cache lifecycle:** cache entries are eagerly invalidated when a workspace is closed via the new `IWorkspaceManager.WorkspaceClosed` event, preventing stale entry accumulation.
- **Shared reference materializer:** `ReferenceLocationMaterializer` extracts the parallel preview-text + containing-symbol resolution pattern into a reusable helper consumed by reference, mutation, and consumer analysis services.
- `CancellationToken` propagation to `SymbolSearchService.CollectSymbols` / `CollectMembers` for cancellable document-symbol walks on large files.
- **Per-workspace concurrent reads**: parallel `find_references` / `project_diagnostics` / `symbol_search` calls against the same workspace now run truly in parallel, bounded only by the global throttle. The benchmark `WorkspaceReadConcurrencyBenchmark` covers the 4-parallel-read shape.

### Fixed

- **Test infrastructure: eliminate MSBuild contention causing intermittent 5-minute timeouts.** Previously every `[ClassInitialize]` called `InitializeServices()` which constructed a new `WorkspaceManager`, and 22 test classes called `WorkspaceManager.LoadAsync(SampleSolutionPath)` independently — spawning 22 separate `MSBuildWorkspace` instances with overlapping file locks on the shared sample fixture. Under load this caused `dotnet build` invocations to hit the service-level cancellation timeout (5 min) and fail. `TestBase` is now idempotent: services are created once per assembly, the workspace manager is disposed in `[AssemblyCleanup]`, and a new `GetOrLoadWorkspaceIdAsync` cache helper makes 22 redundant `LoadAsync` calls collapse into a single MSBuild evaluation. Total test runtime dropped from ~2.3 min to ~2.0 min on a clean run, with the timeout-flake mode eliminated.
- `DependencyAnalysisService.ScanNuGetVulnerabilitiesAsync` no longer hard-codes a 120-second timeout; it now honors `ValidationServiceOptions.VulnerabilityScanTimeout` (still 2 min default). Previously this ignored every `ROSLYNMCP_*_TIMEOUT_SECONDS` env var.
- Symbol handle deserialization rejects malformed base64/JSON input with `ArgumentException` instead of propagating opaque errors.
- `project_diagnostics` tool returns structured compiler/analyzer/workspace error counts with proper camelCase JSON serialization.
- Code metrics: control-flow nesting depth now accounts for braceless bodies.
- `fix_all` tool includes `guidanceMessage` in preview results.
- Type extraction, scaffolding, and project XML formatting correctness improvements.
- Code action provider loading reliability.
- Central Package Management (CPM) version resolution in dependency analysis.
- `test_run` tool generates unique TRX paths per project and reports stderr on failure.
- **Six mis-classified writers correctly take a write lock.** `apply_text_edit`, `apply_multi_file_edit`, `revert_last_apply`, `set_editorconfig_option`, `set_diagnostic_severity`, and `add_pragma_suppression` previously used the bare-`workspaceId` (read) gate while internally calling `WorkspaceManager.TryApplyChanges` or doing disk-direct file mutations. All six now call `RunWriteAsync` explicitly so writes are exclusive against in-flight reads on the same workspace.
- **`workspace_close` TOCTOU race closed.** Added a post-acquire `EnsureWorkspaceStillExists` recheck inside the per-workspace lock so a reader that passed the initial existence check while a concurrent close was queued cannot operate against a workspace that was removed while the reader was waiting for its read lock. The recheck throws `KeyNotFoundException` instead.
- **Nested `LoadGate → RunWriteAsync` deadlock avoided.** `workspace_reload` and `workspace_close` now nest a per-workspace write-lock acquire inside the global load gate so in-flight readers complete before the solution is replaced or removed. To prevent the nested call from consuming two `_globalThrottle` slots (which can deadlock under saturation when `MaxGlobalConcurrency` is small), `WorkspaceExecutionGate` tracks throttle ownership via an `AsyncLocal<int>` re-entrance counter and skips the inner physical wait.

### Changed

- `ValidationServiceOptions` is now a `record` (was `sealed class`), enabling `with` expression updates for cleaner option-binding code in `Program.cs`.
- Test workspace cap raised to 64 (from production default of 8) since the shared `WorkspaceManager` now retains workspaces across the full assembly run instead of per-class disposal cycles.
- Tool descriptions refined for `server_info`, security diagnostics, analyzer listing, undo, and edit tools.
- **`RefactoringTools.ApplyGateKeyFor` helper deleted.** All 12 tool files (~19 sites) that previously composed the synthetic `__apply__:<wsId>` gate key now call `RunWriteAsync(wsId, …)` directly with an explicit `KeyNotFoundException` for stale preview tokens. The bare-`workspaceId` reader sites in 28 tool files (~91 calls) now call `RunReadAsync(workspaceId, …)`. `workspace_load`, `workspace_reload`, and `workspace_close` (`WorkspaceTools`) call `RunLoadGateAsync(…)` for the global lifecycle gate; reload and close additionally nest an inner `RunWriteAsync(workspaceId, …)` so in-flight readers complete before the workspace state is replaced or removed.

### Removed

- **`ROSLYNMCP_WORKSPACE_RW_LOCK` environment variable, `ExecutionGateOptions.UseReaderWriterLock` property, and the legacy per-workspace `SemaphoreSlim` branch in `WorkspaceExecutionGate`.** The per-workspace `AsyncReaderWriterLock` is now the only lock model. The `eng/flip-rw-lock.ps1`, `eng/rw-lock-on.ps1`, `eng/rw-lock-off.ps1`, and `eng/rw-lock-common.ps1` PowerShell helpers used to flip the env var have been deleted along with `ai_docs/reports/2026-04-06-workspace-rw-lock-design-note.md`.
- **`IWorkspaceExecutionGate.RunAsync<T>(string gateKey, …)` compat shim and `LoadGateKey` constant.** Replaced by the explicit `RunLoadGateAsync<T>` method on the interface; production callers (`WorkspaceTools`) and tests have been migrated.

## [1.6.0] - 2026-04-04

### Added

- Integration tests targeting P1/P2 coverage gaps: `HighValueCoverageIntegrationTests`, `BoundedStoreEvictionTests`, `ServiceCollectionExtensionsTests`.
- Sample-solution profiling smoke record and methodology notes in `docs/large-solution-profiling-baseline.md`.
- Post-1.5 surface checks aligned catalog, tier promotions, and integration coverage with CI (`verify-release.ps1`); evidence for later releases uses timestamped files under `ai_docs/audit-reports/`.

### Changed

- **Stable surface:** promoted six read-only analysis/validation tools from experimental to stable — `compile_check`, `list_analyzers`, `find_consumers`, `get_cohesion_metrics`, `find_shared_members`, `analyze_snippet` (`ServerSurfaceCatalog`, `docs/product-contract.md`, `docs/experimental-promotion-analysis.md`).

## [1.5.0] - 2026-04-04

### Added

- P4 MCP surface: NuGet vulnerability scan, `.editorconfig` write, MSBuild evaluation tools, suppression tools, cohesion `excludeTestProjects`, maintainability index, and related refactors; MCP manifest icon and integration tests.
- CodeQL workflow (PR path filter, query suites) with CI policy note.
- Human-facing `docs/setup.md` (build, test, global tool, Docker, CI artifacts); standardized AI doc prompts and indexes; backlog agent-contract and hygiene workflow.
- `docs/parity-gap-implementation-plan.md`; environment bindings for source-gen docs cap, related-test scan cap, and preview TTL (see `ai_docs/runtime.md`).

### Changed

- `WorkspaceManager` / `WorkspaceExecutionGate`: concurrent session limits, workspace validation, apply gate keys, bounded gates; `PreviewStore` and `IWorkspaceManager` extensions; DI registration updates.

### Fixed

- Parity-gap hardening across Roslyn services (diagnostics, fix-all, mutations, flow/control/syntax, unused confidence, TRX aggregation, workspace generated docs, and more from P1–P3 backlog audits).
- Host tools and DTOs (semantic search warning, JSON enums, resource name keys, server/script/syntax surfaces, prompts).

## [1.4.0] - 2026-04-03

### Added

- Security diagnostic surface: `SecurityTestProject` with `InsecureLib`, `SecurityDiagnosticIntegrationTests`, and `SecurityCodeScan` coverage for `get_security_diagnostics` (FEAT-01).
- Workspace-specific resources now discoverable via MCP resource listing; `ServerResources` extended with workspace-scoped entries (AUDIT-14 partial).
- `WorkspaceStatusDto` extended with additional workspace metadata fields.

### Fixed

- `CohesionMetricsDto` contract field alignment (`CohesionAnalysisService`, `ICohesionAnalysisService`).
- `UnusedCodeAnalyzer` improvements: additional symbol categories and reliability fixes.
- `InterfaceExtractionService` edge-case correctness fixes.
- `CodeActionService` action-application reliability.
- `ProjectMutationService` correctness and edge-case handling.
- `WorkspaceManager` concurrency correctness fixes.
- `ServerSurfaceCatalog` surface count updated.

### Changed

- Repository line-ending policy switched to LF; `.gitattributes` enforces LF for all common text assets.

## [1.3.0] - 2026-04-01

### Added

- `GatedCommandExecutor` / `IGatedCommandExecutor` abstraction: shared CLI execution logic extracted from `BuildService` and `TestRunnerService`, eliminating ~200 lines of duplication.
- Offset/limit pagination for `project_diagnostics` and `list_analyzers` tools.
- Evaluated target-framework resolution via `ProjectMetadataParser`: reads MSBuild-evaluated `TargetFramework(s)` instead of raw XML, fixing inherited values from `Directory.Build.props`.
- 11 new integration tests (`BacklogFixTests.cs`); total test count 154.

### Fixed

- `WorkspaceManager` concurrency: apply-gate and version tracking hardened.
- `TestDiscoveryService` reliability for large solutions.
- `FixAllService` exception handling and partial-success reporting.
- `TypeMoveService` correctness for types with nested members.
- `ToolErrorHandler` structured error response improvements.
- `RefactoringService` edge cases for partial classes.
- `FlowAnalysisService` data-flow accuracy improvements.
- `MutationAnalysisService` stale-result detection fixes.

## [1.2.0] - 2026-03-29

### Added

- Offset/limit pagination and filter parameters for `find_references`, `symbol_search`, and `list_members` (AUDIT-24, AUDIT-25, AUDIT-36).

### Fixed

- `compile_check` tool description clarified to reflect actual behaviour (AUDIT-31, AUDIT-26).

## [1.1.0] - 2026-03-27

### Fixed

- `PathFilter` path-validation hardening; symlink/junction resolution correctness (AUDIT-01).
- `CrossProjectRefactoringService` null-safety and project-graph edge cases (AUDIT-22).
- `InterfaceExtractionService` member-extraction edge cases (AUDIT-28).
- `ScaffoldingService` null-safety and generated-code correctness (AUDIT-29).
- `SymbolResolver` resolution reliability for overloaded members (AUDIT-05, AUDIT-19).
- `SymbolServiceHelpers` deduplication and ranking fixes (AUDIT-20, AUDIT-30).
- `TestRunnerService` output-capture and exit-code handling (AUDIT-02).
- `UnusedCodeAnalyzer` false-positive reduction (initial pass).
- `DotnetOutputParser` edge cases for multi-target builds (AUDIT-03, AUDIT-04).
- `ProjectMutationService` property allowlist enforcement tightened (AUDIT-09).
- `SymbolNavigationService` location mapping for generated files (AUDIT-13).
- `TypeMoveService` namespace-update correctness.

### Changed

- Removed dead `CreateFixtureCopy` test fixture helper; added `.editorconfig` to repo root (CODE-05, CODE-06).

## [1.0.0] - 2026-03-26

First stable release.

### Added

- 48 stable tools: workspace management, symbol navigation, references, diagnostics, build/test, refactoring (preview/apply), code actions, cohesion analysis, consumer analysis, bulk refactoring, type extraction, type move, interface extraction, cross-project refactoring, orchestration.
- 55 experimental tools: advanced analysis, direct edits, file operations, project mutations, scaffolding, dead-code removal, syntax inspection, coverage collection.
- 6 stable resources: server catalog, workspace list, workspace status, project graph, diagnostics, source files.
- 16 experimental prompts: error explanation, refactoring suggestions, code review, dependency analysis, test debugging, guided workflows, security review, dead-code audit, complexity review, cohesion analysis, consumer impact, capability discovery, test coverage review.
- Environment variable configuration: `ROSLYNMCP_MAX_WORKSPACES`, `ROSLYNMCP_BUILD_TIMEOUT_SECONDS`, `ROSLYNMCP_TEST_TIMEOUT_SECONDS`, `ROSLYNMCP_PREVIEW_MAX_ENTRIES`.
- Test discovery caching per workspace version for faster repeated lookups.
- Prompt truncation for large workspaces to prevent context overflow.
- XML doc comments on all 15 core service interfaces.
- SecurityCodeScan.VS2019 static analysis integrated globally.
- Code coverage baseline via coverlet.collector (50.3% line, 34.0% branch).
- 121 integration and behavior tests.

### Changed

- Preview store default cap lowered from 50 to 20 entries (configurable via env var).
- `symbol_search` default limit standardized to 50 (was 20).
- Apply gate scoped per-workspace instead of global semaphore.
- File watcher expanded to include `*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.slnx`.
- Reduced cyclomatic complexity in `ParseSemanticQuery`, `SymbolMapper.ToDto`, and `ClassifyReferenceLocation`.
- `test_related` and `test_related_files` descriptions note heuristic matching.

### Security

- Client root path validation with symlink/junction resolution.
- Property allowlist for project mutations.
- Bounded output capture for CLI commands.
- Per-request timeout enforcement (2 minutes default).
- Per-workspace concurrency gating for all apply operations.
- SecurityCodeScan.VS2019 analyzer with zero findings.

## [0.9.0-beta.1] - 2026-03-26

### Added

- Initial public beta release.
- 40 stable tools: workspace management, symbol navigation, diagnostics, build/test, refactoring (preview/apply).
- 50 experimental tools: advanced analysis, direct edits, file operations, project mutations, scaffolding, dead-code removal, cross-project refactoring, orchestration, syntax inspection, code actions, coverage.
- 6 stable resources: server catalog, workspace status, project graph, diagnostics, source files.
- 9 experimental prompts: error explanation, refactoring suggestions, code review, dependency analysis, test debugging, guided workflows.
- Transport-agnostic core with DTOs at boundaries (no raw Roslyn types leak).
- Session-aware workspaces via `workspaceId` with concurrent workspace support.
- Preview/apply with version gating for all mutations.
- Per-workspace and global concurrency throttling.
- File watcher for stale-state detection.
- stderr-only logging with MCP client notification forwarding.
- Graceful shutdown with workspace disposal.
- CI pipeline with release verification and vulnerability scanning.

### Security

- Client root path validation for file operations and text edits.
- Property allowlist for project mutations.
- Bounded output capture for CLI commands.
- Per-request timeout enforcement (2 minutes default).
