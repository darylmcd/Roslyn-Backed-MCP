# test-base-static-service-locator-decomposition — Decompose the TestBase static service locator

**row:** `test-base-static-service-locator-decomposition` · **pri:** `Medium` · **size:** `S` · **deps:** `test-assembly-cleanup-failure-observability,mcp-roots-fixture-lifecycle-consolidation`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/TestServiceContainer.cs`
- `tests/RoslynMcp.Tests/SharedWorkspaceTestBase.cs`

## Acceptance

- [ ] Replace the dozens of mutable static service properties with one immutable, assembly-owned fixture context constructed from `TestServiceContainer`.
- [ ] Separate repository fixture paths and assembly lifecycle ownership from service lookup so initialization and disposal have one explicit owner.
- [ ] Preserve the assembly-shared workspace behavior and source-compatible class cleanup boundary while migrating consumers incrementally or atomically.
- [ ] Add one parallel two-class regression proving both classes receive the same initialized context and that its owned resources are disposed exactly once.

## Evidence

- `TestBase` currently mixes assembly initialization, environment binding, repository path discovery, MCP server ownership, disposal, and more than sixty mutable static service properties; every new service extends the assignment list and obscures ownership.
2026-08-24 current evidence: `TestServiceContainer` resolves to `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`, not the stale root-level path implied by older notes. The pre-refactor Windows profile also measured a 10m17s serialized tail and 123 `[DoNotParallelize]` occurrences; use current semantic ownership rather than the stale anchor when planning.

## Amendment — 2026-09-02 (cold plan-deepener; verified against live source, no code shipped)

**The immutable fixture context this row asks for ALREADY EXISTS.** `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs:7-71` is an `internal sealed class` of 63 `required`/`init` service properties with a single factory `Create(ValidationServiceOptions)` at `:73`. The defect is the **copy layer on top of it**: `TestBase.cs:163` builds the container once, then `:170-232` hand-copies all 63 members into 63 mutable `protected static … { get; private set; }` declarations at `:49-111`. Every new service is written twice inside `TestBase.cs` plus once in the container — that is the "obscures ownership" symptom.

Three unrelated concerns are welded to the same class: repository fixture paths (declared `:113-116`, discovered `:234-237`), MCP path-authorized server ownership (`:15`, `:123-135`), and assembly disposal (`:255-283`, hand-rolled because `TestServiceContainer` does not implement `IDisposable` — `:274` disposes `WorkspaceManager` by hand while `AssemblyCleanup.cs:13` calls in from `[AssemblyCleanup]`).

**Fanout probe (2026-09-02, decisive for sizing).** 159 test files derive from the `TestBase` hierarchy (85 `SharedWorkspaceTestBase` + 58 `IsolatedWorkspaceTestBase` + 16 direct) and **all 159** reference at least one of the 67 inherited static members. `TestServiceContainer` itself has exactly ONE consumer today (`TestBase.cs:163`).

- **REJECTED variant — atomic migration** (delete the statics, make consumers say `Fixture.Services.X`): edits **159 test files**, ~53x over Rule 4 and outright over Rule 5. Not attemptable in one initiative; must NOT be substituted at execute time.
- **CHOSEN variant — forwarder-preserving, 3 files:** every one of the 159 consumers uses the *unqualified inherited name*, which a get-only forwarder preserves byte-for-byte, so the compile of all 159 consumers IS the source-compatibility proof.

**Approach.** New `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs` — one assembly-owned context exposing `Services` (the existing container), a `TestRepositoryFixtures` record for the four paths at `TestBase.cs:234-237`, and the lazy `McpRootsTestServerFactory.Session` from `:15`; it implements `DisposeAsync()`, absorbing `DisposeAssemblyResourcesAsync` (`:255-283`) so disposal gets exactly one owner. `TestBase.cs` replaces the 63 mutable statics and their 63-line assignment block with one `_fixture` field plus 67 get-only expression-bodied forwarders. `InitializeServices()` keeps its `_initLock` double-checked once-per-assembly contract verbatim; `AssemblyCleanup.cs` needs no edit.

**Scope:** production 2 (`TestBase.cs` modified, net −60 lines; `TestInfrastructure/TestAssemblyFixture.cs` new). Tests 1 added: `TestAssemblyFixtureTests.cs` — two parallel `[TestClass]`es proving both observe the same fixture reference and owned resources dispose exactly once (acceptance bullet 4).

**Performance is NOT N/A.** `InitializeServices()`'s comment at `TestBase.cs:140-142` records why the once-per-assembly gate exists: per-class `WorkspaceManager` recreation ran 32x per run causing MSBuild file-lock contention and unbounded hangs; `GetOrLoadWorkspaceIdAsync` (`:294-297`) collapses 22x duplicate `SampleSolution` loads. The `_initLock` pattern and single `WorkspaceIdCache` must survive the move verbatim.

**Stale anchor (fixed here):** the `## Anchors` entry `tests/RoslynMcp.Tests/TestServiceContainer.cs` does not exist — the type lives at `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`. The anchor list also omits `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs` (221 lines, 58 consumers), far more load-bearing than the 8-line `SharedWorkspaceTestBase.cs` it does cite.

**Behavioral risk:** `IsolatedWorkspaceTestBase.cs:113` and `:142` capture `WorkspaceManager.Close`/`LoadAsync` through the inherited static in a nested type's constructor chain — keep the forwarder's null surface identical to today's or assert eagerly.

**Residual accepted by design:** 63 forwarder declarations remain in `TestBase.cs`, so adding a service still touches it once (down from twice). Eliminating that last hop needs the 159-file migration and belongs to a separate row.

**Deps satisfied:** `test-assembly-cleanup-failure-observability` and `mcp-roots-fixture-lifecycle-consolidation` are both absent from the backlog; their artifacts (`Helpers/CleanupFailureCollector.cs`, `Helpers/McpRootsTestServerFactory.cs`) are live and consumed by the code being moved.
