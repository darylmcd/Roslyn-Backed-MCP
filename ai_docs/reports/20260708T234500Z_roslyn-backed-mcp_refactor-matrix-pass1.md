# Refactor Matrix Pass 1 — Roslyn-Backed-MCP

**Date:** 2026-07-08 · **Tool:** `/refactorv2` (Refactor Harness 2.0) · **Scope:** whole-repo · **Backend:** roslyn (workspace `ba7883416485462797c790b4637d10d9`, 7 projects, 694 documents)

## Summary

- 12 refactor slices, 1 vendored (V01, none in this repo), 84 scored cells (12 slices × 7 domain-group lenses)
- Phase 2 adversarial verify: 19 cells verified (High band + extraScrutiny slices S02a/S02b + 1 driver-added security cell); 15 upheld, 3 adjusted, 1 refuted
- Phase 1b cross-slice: 2 confirmed duplication findings
- Final: 83 verified cells, 1 dropped (refuted); 48 backlog initiatives synthesized (11 High, 21 Medium, 16 Low)

## Per-slice verified-cell summary

### S03a-roslyn-refactor-services

- **S03a-roslyn-refactor-services::DG2-cleanliness** [High, score 3] — Core refactor engine (RefactoringService, SymbolRefactorService, ProjectMutationService) is a set of 1500-1800 line god-services with high efferent coupling (22-29) plus widespread copy-pasted helper methods across many files.
- **S03a-roslyn-refactor-services::DG1-design** [Medium, score 2] — RefactoringService is an oversized multi-responsibility class mixing preview/apply orchestration with duplicated MSBuild persistence logic, creating hidden coupling to sibling services like TypeMoveService.
- **S03a-roslyn-refactor-services::DG4-performance** [Medium, score 2] — Several refactor orchestrators (namespace relocation, cross-project interface extraction, solution rebase) do full solution-wide document/AST scans instead of indexed symbol lookups, scaling linearly-to-quadratically with solution size.
- **S03a-roslyn-refactor-services::DG5-security-data** [Medium, score 2] — No secrets/deserialization issues found, but every apply/revert path in this slice writes source and project files directly (no atomic temp+rename), risking partial/corrupted files on interruption — unlike WorkspaceCacheStore/PersistentCompositeStorage's atomic pattern.
- **S03a-roslyn-refactor-services::DG6-testability-obs** [Medium, score 2] — Test coverage is broad across the slice, but several multi-step mutation orchestrators (Composite/ClassSplit/ExtractAndWire/PackageMigration) have zero logging, leaving partial-apply failures unobservable.
- **S03a-roslyn-refactor-services::DG3-robustness** [Medium, score 1] — Slice is heavily hardened (edit validation, MSBuildWorkspace disk-flush workarounds, revert/undo with disk fallback); only minor partial-apply/rollback gaps in two multi-step mutation loops.
- **S03a-roslyn-refactor-services::DG7-config-deps-ergo** [Medium, score 1] — Dependency hygiene and config (PreviewStoreOptions, csproj CVE pinning) are healthy; main ergonomics debt is RefactoringService.cs, a 1555-line god-class with optional-dependency constructor sprawl.

### S03d-roslyn-workspace-infra

- **S03d-roslyn-workspace-infra::DG2-cleanliness** [High, score 2] — WorkspaceManager.cs (1775 LOC) and WorkspaceValidationService.cs (837 LOC) are god-class hubs with several methods at CC 10-18 and maintainability indices in the high-20s/low-30s; no cross-slice duplication found.
- **S03d-roslyn-workspace-infra::DG7-config-deps-ergo** [High, score 2] — WorkspaceManager is a 1775-line god class (232 refs) with several low-maintainability-index methods; DI registration and a per-workspace semaphore cache also show maintainability/hygiene gaps, but config/deps/build ergonomics elsewhere are healthy.
- **S03d-roslyn-workspace-infra::DG1-design** [Medium, score 2] — WorkspaceManager (1775 lines, 232 refs to its interface) bundles session lifecycle with two unrelated embedded subsystems — NuGet restore-staleness detection and analyzer-reference isolation — that should be extracted into their own collaborators.
- **S03d-roslyn-workspace-infra::DG6-testability-obs** [Medium, score 2] — Core workspace infra is well covered by targeted test suites (ChangeTracker, CompilationCache, WorkspaceExecutionGate), but key failure modes (watcher buffer overflow, cache staleness, lock-registry misuse) are neither logged nor directly tested.
- **S03d-roslyn-workspace-infra::DG3-robustness** [Medium, score 1] — Infra is unusually robust (atomic swaps, TOCTOU guards, staleness/retry policies) with only minor gaps: an unbounded per-workspace semaphore dictionary and an unguarded directory-enumeration edge case.
- **S03d-roslyn-workspace-infra::DG5-security-data** [Low, score 2] — PersistentCompositeStorage (opt-in via ROSLYNMCP_PREVIEW_PERSIST_DIR) builds file paths from a client-supplied token without sanitization, allowing path traversal on read/delete.
- **S03d-roslyn-workspace-infra::DG4-performance** [Low, score 0] — No performance/scalability hazards found: CompilationCache is version-keyed with task-sharing, WorkspaceExecutionGate bounds concurrency and rate-limits, FileWatcherService is purely event-driven, and cache I/O sits off the hot request path.

### S04e-host-server-infrastructure

- **S04e-host-server-infrastructure::DG1-design** [Medium, score 2] — Host plumbing is generally well-organized (partial-class catalog decomposition, shared DI extension), but StructuredCallToolFilter is a 1099-line god-class mixing error handling, elicitation policy, and metrics.
- **S04e-host-server-infrastructure::DG2-cleanliness** [Medium, score 2] — StructuredCallToolFilter.cs is a 1099-line god-class mixing dispatch, elicitation, workspace auto-resolve, error formatting and observability with several high-complexity/low-maintainability methods; rest of the slice (Catalog partials, Formatters, Prompts, Resources) is clean.
- **S04e-host-server-infrastructure::DG7-config-deps-ergo** [Medium, score 1] — Infrastructure is well-documented and hygienic (central package mgmt, atomic disk writes, env-var binding); main debt is StructuredCallToolFilter.cs's 1099-line multi-concern file wired into every tool call.
- **S04e-host-server-infrastructure::DG4-performance** [Medium, score 2] — Catalog/reflection indexes are properly Lazy-cached, but the auto-load discovery path re-scans client-root directory trees synchronously and unbounded on every hot-path dispatch while no workspace is loaded.
- **S04e-host-server-infrastructure::DG6-testability-obs** [Low, score 1] — Slice is generally well-tested and well-logged (StructuredCallToolFilter, HostProcessMetadataStore log consistently); 7 of ~17 prompt workflow builders lack any direct test coverage.
- **S04e-host-server-infrastructure::DG3-robustness** [Low, score 0] — Program.cs, ServiceCollectionExtensions, StructuredCallToolFilter, HostProcessMetadataStore, and the catalog/prompt/resource static indexes show deliberate, well-documented hardening (locks, atomic file writes, best-effort swallow-and-log, gate-protected resource reads, immutable Lazy indexes). No robustness defects found in this pass.
- **S04e-host-server-infrastructure::DG5-security-data** [Low, score 0] — No security/persistence defects found: host-process store uses atomic writes and TTL guards, sensitive-field elicitation is refused, path handling is validated, and stdout/stderr are cleanly separated.

### S03c-roslyn-build-test-services

- **S03c-roslyn-build-test-services::DG4-performance** [Medium, score 2] — MsBuildEvaluationService creates a new ProjectCollection and re-parses MSBuild XML on every call with no caching; two callers invoke it once per project per request, causing repeated O(N-projects) disk/XML re-evaluation.
- **S03c-roslyn-build-test-services::DG2-cleanliness** [Medium, score 2] — ScaffoldingService is a 2,844-line, 4-file god class with the slice's highest coupling (Ce=20) and several CC18-20 methods; scattered high-complexity/long-parameter smells elsewhere (TestCoverageCoordinator, EditorConfigService) plus a cross-file duplicate assembly loader in FixAllService.
- **S03c-roslyn-build-test-services::DG6-testability-obs** [Medium, score 2] — Most services are covered by integration tests and half use ILogger, but TestCoverageCoordinator's parsing logic is completely untested and several services (ScaffoldingService, EditorConfigService, SuppressionService, MsBuildEvaluationService) swallow exceptions with no logging.
- **S03c-roslyn-build-test-services::DG1-design** [Medium, score 2] — ScaffoldingService is a single partial class (2,844 lines, 4 files) blending type/test/batch/first-test scaffolding with several complexity-19 methods and an 11-parameter method; rest of the slice is cleanly one-class-per-file.
- **S03c-roslyn-build-test-services::DG7-config-deps-ergo** [Low, score 1] — Mostly healthy, well-documented services with sound options/caching patterns; minor debt is a stale narrowing comment/dead branch in NuGetDependencyService and two low-maintainability-index, high-complexity methods plus a 1100-line god file.
- **S03c-roslyn-build-test-services::DG3-robustness** [Low, score 1] — Slice is well-hardened (SemaphoreSlim gating, Interlocked state, version-keyed caches, consistent OperationCanceledException rethrow); only minor concurrency edge cases found, no correctness-breaking defects.
- **S03c-roslyn-build-test-services::DG5-security-data** [Low, score 1] — Slice is otherwise sound (temp dirs are GUID-scoped and cleaned up, exception text is deliberately withheld from callers); only notable issue is the fail-open default in SecurityOptions.

### S03b-roslyn-analysis-services

- **S03b-roslyn-analysis-services::DG2-cleanliness** [Medium, score 2] — Namespace-walk and FindContainingType logic is reimplemented 3x across CouplingAnalysisService/SymbolSearchService/ImpactSweepService, ReferenceService triple-duplicates an overload, and several analysis methods have MI<35 or 10-parameter signatures.
- **S03b-roslyn-analysis-services::DG4-performance** [Medium, score 2] — Most services in this slice already parallelize/cache SymbolFinder fan-outs, but CouplingAnalysisService is documented as near its 15s budget and ImpactSweepService redundantly re-scans all solution types per DTO sibling with no caching.
- **S03b-roslyn-analysis-services::DG1-design** [Low, score 2] — Otherwise well-factored one-service-per-tool slice, but three services (Consumer/TypeConsumers/MutationAnalysis) independently reimplement near-identical reference-site classification logic, risking drift between tools answering the same question.
- **S03b-roslyn-analysis-services::DG6-testability-obs** [Low, score 1] — Slice has broad integration-test coverage across nearly all 28 services (including a contract test for the one swallowed-exception path found), but roughly half the services carry zero ILogger instrumentation, leaving production failures unobservable.
- **S03b-roslyn-analysis-services::DG3-robustness** [Low, score 0] — Robustness practices are strong: cancellation tokens threaded consistently, exception filters preserve OperationCanceledException, documented thread-safe Parallel.ForEachAsync with ConcurrentBag accumulation, and type-argument/array accesses are length-guarded before indexing.
- **S03b-roslyn-analysis-services::DG7-config-deps-ergo** [Low, score 1] — Dependency and package config are clean (well-documented CVE override); minor debt is duplicated hardcoded parallelism/timeout magic numbers not centralized into shared options.
- **S03b-roslyn-analysis-services::DG5-security-data** [Low, score 0] — No secrets, persistence, or untrusted deserialization in this slice; analyzer/codefix assembly loading reuses existing project AnalyzerReferences (no new attack surface) and SemanticGrepService bounds regex evaluation with a timeout.

### S04a-host-refactor-tools

- **S04a-host-refactor-tools::DG6-testability-obs** [Medium, score 2] — About a third of the 30+ tool endpoints in this slice (type/interface extraction, type-move, restructure, split-service, record-satellite refactors) have zero test coverage; per-tool observability is intentionally centralized via StructuredCallToolFilter, which is healthy.
- **S04a-host-refactor-tools::DG3-robustness** [Medium, score 2] — Tool shims consistently route through WorkspaceExecutionGate's robust per-workspace read/write locking; the one gap is ApplyWithVerifyTool's apply-verify-revert chain lacking cancellation-safe compensation between steps.
- **S04a-host-refactor-tools::DG5-security-data** [Medium, score 3] — Most write-capable refactoring tools accept a raw absolute filePath and skip ClientRootPathValidator (used only in EditTools/MultiFileEditTools), letting mutations bypass the MCP client's sanctioned-root boundary.
- **S04a-host-refactor-tools::DG1-design** [Low, score 1] — Slice is a well-modeled, consistently documented family of thin Preview/Apply tool shims; ApplyWithVerifyTool is the one outlier embedding real orchestration logic in the host layer instead of Core.
- **S04a-host-refactor-tools::DG4-performance** [Low, score 2] — apply_with_verify always runs an unscoped full-solution compile check twice per apply, unlike the sibling verify path which scopes to the owning project.
- **S04a-host-refactor-tools::DG2-cleanliness** [Low, score 1] — Slice is largely clean thin shims via ToolDispatch; residual debt is a handful of tools not yet migrated to the shared dispatch helper, duplicating its RunReadAsync/serialize boilerplate. Low severity, already tracked in-repo as a WS1 migration in progress.
- **S04a-host-refactor-tools::DG7-config-deps-ergo** [Low, score 0] — Tool shim files are consistently structured, well-documented with migration rationale, and the host csproj packaging/config is clearly justified; only a trivial duplicated allowlist string was found.

### S02a-core-dtos

- **S02a-core-dtos::DG1-design** [Medium, score 1] — Models slice is largely sound (immutable records, no mutable List leaks) but ~8 DTOs hand-roll location-span fields instead of composing the existing LocationDto value object.
- **S02a-core-dtos::DG6-testability-obs** [Low, score 1] — Slice is ~90 near-pure record DTOs; the two files with real branching logic (WorkspaceStatusSummaryDto.From, SymbolLocator.Validate) are mostly but not fully unit-tested, and DTOs carry no observability surface by design.
- **S02a-core-dtos::DG3-robustness** [Low, score 1] — Slice is almost entirely immutable sealed records (no mutable classes/setters); the only robustness gap found is one array-typed collection property breaking the IReadOnlyList/value-equality convention used elsewhere.
- **S02a-core-dtos::DG5-security-data** [Low, score 0] — DTOs are immutable sealed records exposing only IReadOnlyList/IReadOnlyDictionary collections, no secrets/credentials, and no direct persistence or unsafe deserialization logic; healthy.
- **S02a-core-dtos::DG7-config-deps-ergo** [Low, score 1] — Models layer is clean: zero external deps beyond System.Text.Json, minimal csproj, consistent record/JsonPropertyName usage. Only minor gotcha: list-typed record members break structural equality with no observed reliance on it.
- **S02a-core-dtos::DG2-cleanliness** [Low, score 1] — Pure record DTOs with no embedded logic or excessive complexity, but a recurring minor DRY smell: many DTOs (SymbolDto, DiagnosticDto, TypeUsageDto, etc.) hand-roll the same FilePath/Start/End location quartet instead of composing LocationDto.

### S04b-host-analysis-tools

- **S04b-host-analysis-tools::DG6-testability-obs** [Medium, score 2] — One shared test file covers only ~4 of ~130 tool endpoints across 15 files (destructive apply tools untested); zero per-tool logging/observability in the entire slice.
- **S04b-host-analysis-tools::DG4-performance** [Medium, score 2] — Most tools here are well-bounded (pagination, hard caps, summary modes), but find_references_bulk lacks its documented 50-symbol cap and find_consumers has no pagination at all.
- **S04b-host-analysis-tools::DG7-config-deps-ergo** [Medium, score 2] — Pagination/limit handling is duplicated ad hoc across ~15 tool files with inconsistent hardcoded defaults, and only one of many call sites clamps an upper bound, risking unbounded payloads and inconsistent DX.
- **S04b-host-analysis-tools::DG1-design** [Low, score 1] — Tool-wrapper design is consistent and thin across the slice, but AdvancedAnalysisTools.cs is a grab-bag class and SymbolTools.cs mixes elicitation infra into an 18-tool 988-line file — minor cohesion debt, not correctness risk.
- **S04b-host-analysis-tools::DG2-cleanliness** [Low, score 1] — Slice is largely clean — heavy, consistent reuse of shared helpers (gate.RunReadAsync, ParameterValidation, SymbolLocatorFactory) with no real duplication in-scope; only mild complexity/size debt concentrated in SymbolTools.cs.
- **S04b-host-analysis-tools::DG5-security-data** [Low, score 2] — Five endpoints (CodeActionTools, FlowAnalysisTools, OperationTools) accept mandatory absolute filePath without the ClientRootPathValidator root-scoping check applied elsewhere in the same tool surface, an inconsistent defense-in-depth gap.
- **S04b-host-analysis-tools::DG3-robustness** [Low, score 0] — Tool endpoints are thin, stateless dispatchers with no shared mutable state; errors and concurrency are consistently delegated to the shared WorkspaceExecutionGate/ToolDispatch layer with structured not-found envelopes and per-item error isolation in bulk ops.

### S04c-host-build-test-tools

- **S04c-host-build-test-tools::DG3-robustness** [Medium, score 2] — Solid, consistent dispatch/error-envelope patterns across the slice, but test_coverage silently swallows real cancellation as a fake timeout result, and workspace_fork_apply's directory copy ignores the CancellationToken entirely.
- **S04c-host-build-test-tools::DG4-performance** [Medium, score 2] — workspace_fork_apply's synchronous recursive directory copy/delete is the main scalability hazard in this slice; sequential per-project coverage runs are a secondary, minor concern.
- **S04c-host-build-test-tools::DG5-security-data** [Medium, score 2] — workspace_fork_apply copies the whole source tree (no secret-file exclusions) into on-disk forks that can be retained indefinitely via retention=keep; coverage output also lands in shared OS temp with no cleanup.
- **S04c-host-build-test-tools::DG6-testability-obs** [Medium, score 2] — 8 of 12 tool files (Scaffolding, Security, Suppression, FixAll, EditorConfig, MSBuild, Scripting, TestReferenceMap) have no direct Tools-layer test — only their Core services are tested, leaving MCP-facing glue unverified.
- **S04c-host-build-test-tools::DG2-cleanliness** [Medium, score 2] — 9 of 12 files are clean thin dispatch shims, but ValidationBundleTools.WorkspaceForkApply and TestCoverageTools.RunCoveragePassAsync embed heavy filesystem/process orchestration in the tool layer (MI 34-38, CC 12-14) with minor cross-file dup.
- **S04c-host-build-test-tools::DG1-design** [Low, score 2] — Two of twelve tool files (ValidationBundleTools, TestCoverageTools) embed substantial process/filesystem orchestration logic in the MCP adapter layer, breaking the thin-shim pattern the rest of the slice follows cleanly.
- **S04c-host-build-test-tools::DG7-config-deps-ergo** [Low, score 1] — Slice is a clean, consistent thin-dispatch tool layer overall; main debt is TestCoverageTools' unbounded temp-dir leak and an inconsistent hardcoded dotnet-path/timeout in ValidationBundleTools' fork restore.

### S04d-host-workspace-infra-tools

- **S04d-host-workspace-infra-tools::DG1-design** [Medium, score 2] — WorkspaceTools.cs is an oversized (1165-line) god-class mixing lifecycle, health, readiness-report, support-bundle, and source-access responsibilities; rest of the slice (ToolDispatch, ToolErrorHandler, small *Tools files) is cleanly bounded.
- **S04d-host-workspace-infra-tools::DG2-cleanliness** [Medium, score 1] — Mostly clean, well-documented shim/dispatch code; main debt is WorkspaceTools.cs's god-class scope and a handful of methods (complexity 10-13, MI 37-47) outgrowing the tool-shim pattern.
- **S04d-host-workspace-infra-tools::DG6-testability-obs** [Low, score 1] — Core dispatch/error-handling plumbing (ToolDispatch, ToolErrorHandler, WorkspaceTools) is well tested and instrumented with metrics/structured logging; the shared SymbolLocatorFactory branch logic and a couple of thin wrapper tools lack direct unit coverage.
- **S04d-host-workspace-infra-tools::DG4-performance** [Low, score 1] — No serious scalability hazards; this slice is thin, well-capped dispatch code. Only minor overhead: uncached reflection scan in PromptShimTools and synchronous per-parent-dir I/O in ClientRootPathValidator.
- **S04d-host-workspace-infra-tools::DG7-config-deps-ergo** [Low, score 0] — Config knobs (ROSLYNMCP_* env vars, timeouts, caps) are consistently documented inline where used, dispatch/error-handling helpers are centralized and well-commented, no TODOs or dead config found.
- **S04d-host-workspace-infra-tools::DG3-robustness** [Low, score 0] — Robustness is healthy: gate-scoped read/write dispatch, structured exception classification (stale-token, eviction, reload races), fail-open/closed path validation, and non-fatal process-drain cleanup are all deliberately handled with documented rationale.
- **S04d-host-workspace-infra-tools::DG5-security-data** [Low, score 0] — Path validation (symlink-resolved, root-sanctioned), JSON serialization, and error envelopes are defensively written with truncation/bounding; no security or persistence defects found in-scope.

### S01-analyzer-catalog

- **S01-analyzer-catalog::DG6-testability-obs** [Low, score 1] — Well-tested overall (4 dedicated analyzer tests + slice-field tests) but the non-literal-catalog-name suppression branch that silences RMCP001 is completely uncovered.
- **S01-analyzer-catalog::DG2-cleanliness** [Low, score 1] — No duplication or cross-file coupling issues; two syntax-node callback methods (AnalyzeInvocation, AnalyzeMethodAttributes) carry moderate cyclomatic complexity (13-17) and low maintainability indexes (35-47) worth a light extract-method pass.
- **S01-analyzer-catalog::DG4-performance** [Low, score 0] — Both analyzers register lean, early-bailing SyntaxNodeAction/SymbolAction callbacks with O(1) concurrent-dictionary lookups; no quadratic scans or unbounded allocations found.
- **S01-analyzer-catalog::DG7-config-deps-ergo** [Low, score 1] — Config/deps/ergonomics are healthy: csproj is well-documented, correctly scoped (netstandard2.0, PrivateAssets=all), and cleanly wired into Host.Stdio and tests; only nit is permanently-empty Shipped.md.
- **S01-analyzer-catalog::DG1-design** [Low, score 1] — Core analyzer class design is sound (clear state machine, well-documented), but the project bundles an unrelated StdoutWriteAnalyzer under the ServerSurfaceCatalog name/namespace — a minor SRP/naming mismatch at the project boundary.
- **S01-analyzer-catalog::DG5-security-data** [Low, score 0] — Both analyzers are pure in-memory Roslyn syntax/symbol analyzers with no I/O, secrets, persistence, or serialization; DG5 domains do not apply.
- **S01-analyzer-catalog::DG3-robustness** [Low, score 0] — Both analyzers correctly use EnableConcurrentExecution with ConcurrentDictionary/Interlocked/Volatile for shared state, and degrade to no-ops when expected types/assemblies are absent rather than throwing.

### S02b-core-service-contracts

- **S02b-core-service-contracts::DG1-design** [Low, score 0] — Sampled ~15 of 84 files across the slice: consistently small, single-purpose interfaces (ISP-compliant) with thorough XML docs and clear preview/apply contract conventions; no design defects found.
- **S02b-core-service-contracts::DG7-config-deps-ergo** [Low, score 1] — Services folder itself is clean (no cross-layer usings, well-documented option records); the only DG7 debt is two static singleton patterns (WorkspaceEvictionRegistry, AmbientGateMetrics) that bypass DI for cross-layer state sharing.
- **S02b-core-service-contracts::DG4-performance** [Low, score 0] — Contracts consistently bound output (limit/maxResults/summary caps) and BoundedStore is a small-N TTL store; no unbounded-collection or blocking hazards found at this interface layer.
- **S02b-core-service-contracts::DG6-testability-obs** [Low, score 0] — Slice is mostly pure service contracts plus a few well-documented, well-tested infrastructure classes (BoundedStore, AmbientGateMetrics, WorkspaceEvictedException/Registry with test Reset hooks) feeding rich per-request _meta observability; no testability or observability gaps found.
- **S02b-core-service-contracts::DG2-cleanliness** [Low, score 1] — Mostly clean, well-documented interface segregation (84 files), but real smells surfaced: overlapping consumer-analysis services, duplicated overload pairs instead of optional params, and a repeated long-parameter-list pattern in IEditService.
- **S02b-core-service-contracts::DG3-robustness** [Low, score 0] — Slice is nearly all interfaces/DTOs/exceptions; the few concrete stateful types (BoundedStore, AmbientGateMetrics, WorkspaceEvictionRegistry) use correct concurrency primitives. No robustness defects found within scope.
- **S02b-core-service-contracts::DG5-security-data** [Low, score 0] — Slice is pure interface/contract/exception definitions with no secrets, crypto, or serialization logic; IWorkspaceCacheStore explicitly documents safe atomic-write/versioning persistence contracts. No security or data-integrity defects found.

## Cross-slice findings

- **IsVsMsbuildRequiredMessage substring-matching logic duplicated between Core DTO and Roslyn Helpers** (cross-slice-duplication, severity 2, spans S02a-core-dtos, S03d-roslyn-workspace-infra)
- **CSharp.Features assembly-loading helper duplicated verbatim across analysis and build/test-tooling service slices** (cross-slice-duplication, severity 1, spans S03b-roslyn-analysis-services, S03c-roslyn-build-test-services)

## Dedup / drop rationale

- 1 cell dropped: `S02a-core-dtos::DG4-performance` — refuted at verify (recursive tree DTOs already depth/size-capped at construction sites in SyntaxService/OperationService).
- No synthesis proposals collided with existing backlog ids (30 live ids checked; 0 dupes).

## Emitted initiatives

- `refactoringservice-god-class-decomposition` [High/S] — cells: S03a-roslyn-refactor-services::DG1-design, S03a-roslyn-refactor-services::DG7-config-deps-ergo, S03a-roslyn-refactor-services::DG2-cleanliness, S03a-roslyn-refactor-services::DG4-performance, S03a-roslyn-refactor-services::DG5-security-data
- `refactor-services-duplicate-code-sweep` [High/M] — cells: S03a-roslyn-refactor-services::DG2-cleanliness
- `workspace-manager-decompose-restore-and-analyzer-subsystems` [High/S] — cells: S03d-roslyn-workspace-infra::DG2-cleanliness, S03d-roslyn-workspace-infra::DG7-config-deps-ergo, S03d-roslyn-workspace-infra::DG1-design
- `workspace-validation-service-validateinternal-decompose` [High/S] — cells: S03d-roslyn-workspace-infra::DG2-cleanliness
- `structuredcalltoolfilter-god-class-decompose` [High/S] — cells: S04e-host-server-infrastructure::DG1-design, S04e-host-server-infrastructure::DG2-cleanliness, S04e-host-server-infrastructure::DG7-config-deps-ergo (evidence 1)
- `security-options-fail-open-default` [High/S] — cells: S03c-roslyn-build-test-services::DG5-security-data
- `host-refactor-tools-root-boundary-validation` [High/M] — cells: S04a-host-refactor-tools::DG5-security-data
- `apply-with-verify-cancellation-and-compile-scope` [High/M] — cells: S04a-host-refactor-tools::DG3-robustness, S04a-host-refactor-tools::DG4-performance
- `host-analysis-tools-missing-clientroot-path-validation` [High/M] — cells: S04b-host-analysis-tools::DG5-security-data
- `workspace-fork-apply-robustness-cancellation` [High/M] — cells: S04c-host-build-test-tools::DG3-robustness, S04c-host-build-test-tools::DG4-performance
- `workspace-fork-apply-security-hardening` [High/M] — cells: S04c-host-build-test-tools::DG5-security-data, S04c-host-build-test-tools::DG7-config-deps-ergo
- `refactor-services-non-atomic-write-rollback` [Medium/M] — cells: S03a-roslyn-refactor-services::DG5-security-data, S03a-roslyn-refactor-services::DG3-robustness, S03a-roslyn-refactor-services::DG6-testability-obs
- `refactor-services-full-solution-scan-perf` [Medium/M] — cells: S03a-roslyn-refactor-services::DG4-performance
- `workspace-infra-resource-cleanup-hygiene` [Medium/M] — cells: S03d-roslyn-workspace-infra::DG7-config-deps-ergo, S03d-roslyn-workspace-infra::DG3-robustness
- `solutiondiscoveryhelper-hotpath-perf` [Medium/M] — cells: S04e-host-server-infrastructure::DG4-performance
- `prompt-workflows-missing-test-coverage` [Medium/M] — cells: S04e-host-server-infrastructure::DG6-testability-obs
- `msbuild-evaluation-uncached-perf` [Medium/M] — cells: S03c-roslyn-build-test-services::DG4-performance
- `scaffoldingservice-god-class-decompose` [Medium/M] — cells: S03c-roslyn-build-test-services::DG2-cleanliness, S03c-roslyn-build-test-services::DG1-design
- `build-test-services-swallowed-exceptions-no-logging` [Medium/M] — cells: S03c-roslyn-build-test-services::DG6-testability-obs
- `analysis-services-dedup-type-traversal-helpers` [Medium/M] — cells: S03b-roslyn-analysis-services::DG2-cleanliness (namespace/type-walk dupes)
- `analysis-services-uncached-full-solution-scans` [Medium/M] — cells: S03b-roslyn-analysis-services::DG4-performance
- `apply-with-verify-undo-thin-shim-extraction` [Medium/M] — cells: S04a-host-refactor-tools::DG1-design
- `core-dto-location-quartet-consolidation-primary` [Medium/M] — cells: S02a-core-dtos::DG1-design, S02a-core-dtos::DG2-cleanliness
- `core-dto-location-quartet-consolidation-secondary` [Medium/M] — cells: S02a-core-dtos::DG1-design
- `paramvalidation-pagination-upper-bound-clamp` [Medium/S] — cells: S04b-host-analysis-tools::DG7-config-deps-ergo
- `analysis-tools-pagination-clamp-rollout` [Medium/M] — cells: S04b-host-analysis-tools::DG4-performance, S04b-host-analysis-tools::DG7-config-deps-ergo
- `workspace-fork-apply-extract-service` [Medium/M] — cells: S04c-host-build-test-tools::DG1-design, S04c-host-build-test-tools::DG2-cleanliness
- `host-tools-layer-test-coverage-gap` [Medium/M] — cells: S04c-host-build-test-tools::DG6-testability-obs
- `workspacetools-god-class-decomposition` [Medium/S] — cells: S04d-host-workspace-infra-tools::DG1-design, S04d-host-workspace-infra-tools::DG2-cleanliness (WorkspaceTools god-file overlap)
- `host-tools-complexity-hotspot-cleanup` [Medium/M] — cells: S04d-host-workspace-infra-tools::DG2-cleanliness
- `analyzer-catalog-untested-drift-suppression-branches` [Medium/S] — cells: S01-analyzer-catalog::DG6-testability-obs
- `dedupe-csharp-features-assembly-load-helper` [Medium/M] — cells: CSharp.Features assembly-loading helper duplicated verbatim across analysis and build/test-tooling service slices
- `persistent-composite-storage-token-path-traversal` [Low/S] — cells: S03d-roslyn-workspace-infra::DG5-security-data
- `nugetversionchecker-httpclient-factory` [Low/S] — cells: S04e-host-server-infrastructure::DG7-config-deps-ergo (evidence 2)
- `analysis-services-dedup-reference-classifiers` [Low/M] — cells: S03b-roslyn-analysis-services::DG1-design
- `analysis-services-hardcoded-parallelism-clamp-magic-numbers` [Low/M] — cells: S03b-roslyn-analysis-services::DG7-config-deps-ergo
- `host-tools-todispatch-manual-body-dedup` [Low/M] — cells: S04a-host-refactor-tools::DG2-cleanliness
- `core-dto-fileeditsdto-array-to-readonlylist` [Low/S] — cells: S02a-core-dtos::DG3-robustness
- `core-dto-symbollocator-validate-unit-tests` [Low/S] — cells: S02a-core-dtos::DG6-testability-obs
- `host-tools-cohesion-split` [Low/M] — cells: S04b-host-analysis-tools::DG1-design, S04b-host-analysis-tools::DG2-cleanliness
- `symbollocatorfactory-drift-tool-test-gap` [Low/M] — cells: S04d-host-workspace-infra-tools::DG6-testability-obs
- `host-tools-prompt-reflection-and-path-io-perf` [Low/M] — cells: S04d-host-workspace-infra-tools::DG4-performance
- `stdoutwrite-analyzer-complexity-split` [Low/S] — cells: S01-analyzer-catalog::DG2-cleanliness
- `servercatalog-analyzer-complexity-split` [Low/S] — cells: S01-analyzer-catalog::DG2-cleanliness
- `stdoutwrite-analyzer-project-misplacement` [Low/M] — cells: S01-analyzer-catalog::DG1-design
- `static-singleton-di-bypass-core-services` [Low/M] — cells: S02b-core-service-contracts::DG7-config-deps-ergo, S02b-core-service-contracts::DG2-cleanliness
- `consolidate-consumer-analysis-services` [Low/M] — cells: S02b-core-service-contracts::DG2-cleanliness
- `iedit-service-param-object` [Low/S] — cells: S02b-core-service-contracts::DG2-cleanliness
