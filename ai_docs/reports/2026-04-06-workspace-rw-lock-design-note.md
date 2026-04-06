# Workspace read-write lock — design note (perf lever #6)

<!-- purpose: Spike findings and proposed approach for converting the per-workspace mutex in WorkspaceExecutionGate to a reader-writer model. No code yet. -->

**Status:** spike + design only. Implementation deferred to a follow-up PR.
**Owner:** perf quick-wins program (deep-review-2026-04-06).
**Related:** PRs #74 (perf batch 1), #75 (perf batch 2 — `CompilationCache`).

## Why this exists

The deep-review survey identified `WorkspaceExecutionGate`'s per-workspace `SemaphoreSlim(1,1)` as a serialization bottleneck for read-heavy tools running against the same workspace. Two parallel `find_references` calls on workspace `foo` cannot run concurrently — they queue behind the same gate.

Before designing a fix, this note captures what the current locking model **actually does**, because the survey description ("a single semaphore per workspace serializes all work") turned out to be incomplete.

## Audit findings — current locking model

### 1. The gate has *three* key namespaces, not one

`WorkspaceExecutionGate.RunAsync` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:44-92`) routes the call to a `SemaphoreSlim` chosen by string key:

| Key shape | Purpose | Backing semaphore |
|---|---|---|
| `__load__` (well-known) | `workspace_load`, `workspace_reload` | shared `_loadGate` |
| `<workspaceId>` | Read tools that take a workspaceId | per-workspace entry in `_workspaceGates` |
| `__apply__:<workspaceId>` | Code-fix / refactor / project-mutation **apply** operations | **separate** per-workspace entry in `_workspaceGates` |

The dictionary key is the raw string, so `foo` and `__apply__:foo` map to **different** semaphore instances.

### 2. Reads and applies on the same workspace are NOT serialized against each other

Because `foo` and `__apply__:foo` are different keys, an in-flight `find_references(foo)` does not block `code_fix_apply(foo)` and vice versa. The current code intentionally lets reads run concurrently with writes.

This is only safe because:

- Roslyn `Solution` is immutable. Reads operate on whatever snapshot they captured via `IWorkspaceManager.GetCurrentSolution`. When an apply runs, it produces a *new* snapshot; concurrent readers continue to see their stale (but consistent) one.
- `MSBuildWorkspace.TryApplyChanges` is internally synchronized.
- `WorkspaceManager.TryApplyChanges` (`src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:339-360`) bumps `session.Version` on success, which transparently invalidates anything keyed by version (e.g. the new `CompilationCache`).

### 3. The serialization that *does* exist is read-vs-read on the same workspace

Two concurrent `find_references(foo)` calls both want gate `foo`, so they queue. **This is the bottleneck the original survey was pointing at.**

### 4. The global throttle is still in front of all of this

`_globalThrottle = SemaphoreSlim(max(2, ProcessorCount))` (`WorkspaceExecutionGate.cs:30`) bounds the total number of concurrent operations across the whole server. This is unrelated to the per-workspace gating and stays exactly as is.

### 5. Caller surface area

`grep -r "RunAsync(.*workspaceId\|RunAsync(.*LoadGateKey\|RunAsync(.*__apply__" src/RoslynMcp.Host.Stdio/Tools/` returns ~100 call sites across 33 tool files. Categorized by gate-key intent:

| Category | Gate key today | Tool files (representative) |
|---|---|---|
| Workspace lifecycle | `__load__` | `WorkspaceTools` |
| Apply (writes via `TryApplyChanges`, project file mutation, file CRUD) | `__apply__:<ws>` | `RefactoringTools`, `BulkRefactoringTools`, `CrossProjectRefactoringTools`, `TypeMoveTools`, `TypeExtractionTools`, `InterfaceExtractionTools`, `CodeActionTools`, `FixAllTools`, `ProjectMutationTools`, `ScaffoldingTools`, `EditTools`, `MultiFileEditTools`, `FileOperationTools`, `EditorConfigTools`, `SuppressionTools`, `OrchestrationTools` (some), `UndoTools` |
| Read (analysis, navigation, search, diagnostics) | `<ws>` | `AnalysisTools`, `AdvancedAnalysisTools`, `SymbolTools`, `ConsumerAnalysisTools`, `FlowAnalysisTools`, `CohesionAnalysisTools`, `SyntaxTools`, `ValidationTools`, `AnalyzerInfoTools`, `DeadCodeTools`, `CompileCheckTools`, `TestCoverageTools`, `SecurityTools`, `OperationTools`, `MSBuildTools`, `OrchestrationTools` (some) |

**Note on `ValidationTools` and `BuildService`/`TestRunnerService`**: these tools shell out to `dotnet build` / `dotnet test` against the on-disk solution. They do not mutate the in-memory `Solution`, but they *do* race with on-disk file mutations (`apply_text_edit`, project mutations). They're currently classified as reads against the workspace, which is consistent with how they're used today and is not changed by this design.

## Proposed model

Replace the per-workspace `SemaphoreSlim(1,1)` with `ReaderWriterLockSlim` (or equivalent `AsyncReaderWriterLock`) per workspace, **and merge the `__apply__:foo` gate into the same lock as `foo`** so that writes are properly exclusive against reads on the same workspace.

| Operation | Today | Proposed |
|---|---|---|
| `workspace_load` / `workspace_reload` | `__load__` shared mutex | unchanged — still `__load__` |
| Read tools (analysis, navigation, search, diagnostics) | per-workspace mutex via `<ws>` | per-workspace **read** lock |
| Apply tools (refactor apply, code-fix apply, project mutation, file CRUD) | per-workspace mutex via `__apply__:<ws>` (independent of read gate!) | per-workspace **write** lock |

### What this gains

- **N concurrent reads on the same workspace** instead of 1. Two parallel `find_references` / `project_diagnostics` / `symbol_search` calls run truly in parallel, bounded only by `_globalThrottle`. This is the intended unlock.
- **Writes are now correctly exclusive against reads on the same workspace** — a latent correctness improvement. Today's model relies entirely on Solution immutability + version-keyed caches for stale-snapshot safety; the new model adds an actual happens-before edge so an applier sees no in-flight readers operating against the workspace's mutable state (e.g., on-disk files for tools that touch them).

### What needs care

1. **Reader/writer classification per call site.** Every `gate.RunAsync(...)` in `src/RoslynMcp.Host.Stdio/Tools/` must be classified explicitly. The category table above is the starting point but needs a manual verification pass — anything mis-classified as a reader that actually mutates state is a bug.
2. **`OrchestrationTools` is mixed.** Composite operations sometimes include applies. The gate key is currently chosen by a `wsId != null ? "__apply__:..." : "__apply__"` helper. This needs to become writer-by-default for any composite that may apply, and reader for read-only composites.
3. **`UndoTools.revert_last_apply` is a writer**, not a reader, despite the "revert" name — it calls back into the workspace mutation path.
4. **Reader/writer fairness.** `ReaderWriterLockSlim` defaults to non-reentrant, no-recursion mode with reader preference. We want **writer preference** to avoid writer starvation in read-heavy workloads (the common case for an MCP server). This needs an explicit policy choice when constructing the lock.
5. **Async-aware vs sync.** `ReaderWriterLockSlim` is sync-only and ties up a thread. We use `await` heavily inside gated actions, so we need either:
   - A third-party `AsyncReaderWriterLock` (e.g. `Nito.AsyncEx.AsyncReaderWriterLock` — already a transitive dep? to verify), or
   - A hand-rolled implementation built on `SemaphoreSlim` (small but easy to get wrong).
6. **`workspace_status` and other "metadata" calls** that currently take `__load__` because they read load-time state may need to become readers against the per-workspace lock instead. To verify per call site.
7. **`__load__` interaction.** Today, `__load__` is a separate semaphore and does not interact with per-workspace gates. With per-workspace RW locks, `workspace_reload(foo)` should also acquire workspace `foo`'s **write** lock, not just `__load__`, so that in-flight readers on `foo` complete before the reload yanks the solution out from under them. This is a behavioral change worth calling out.

### Validation plan

1. Land the RW lock behind a feature flag (env var: `ROSLYNMCP_WORKSPACE_RW_LOCK=1`), default off.
2. Run the full test matrix in both modes; gate any merge on parity.
3. Add a benchmark: two parallel `find_references` against the same workspace. Today they serialize; under the new model they should run in roughly the time of one.
4. Soak-test against a large solution (the Roslyn repo itself is the obvious target) for a 30-minute mixed-workload run.
5. After at least one full release cycle of opt-in success, flip the default to on and remove the flag in a follow-up.

### What this design *does not* propose

- Splitting reads further into "pure semantic" vs "filesystem-touching" lanes. Premature.
- Removing `_globalThrottle`. It still serves its purpose (bounding total CPU/memory).
- Touching `__load__`'s shared-across-workspaces semantics. That's an unrelated lever.

## Open questions

1. Is `Nito.AsyncEx` already a transitive dependency, or do we need to add it (or hand-roll the async RW lock)?
2. Are there tools currently using gate key `<ws>` that internally call `TryApplyChanges`? If so they're misclassified today and would fail under the new model — worth a one-pass audit before implementation.
3. Should `workspace_reload` block on the per-workspace write lock, or keep its current "fire and forget the old solution" semantics? (Recommendation: block, for predictability.)

## Decision needed before implementation

- Approve / reject the proposal.
- Pick async lock implementation (`Nito.AsyncEx` vs hand-rolled).
- Approve the feature-flag gating + soak strategy.

Once these are answered, the implementation is a single PR scoped to:

- New `IWorkspaceLock` (or extend `IWorkspaceExecutionGate`) abstraction
- Per-workspace `AsyncReaderWriterLock` instances
- Caller-by-caller migration with explicit read/write classification at every `RunAsync` site
- Feature flag + opt-in
- Benchmarks
