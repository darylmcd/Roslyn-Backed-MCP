---
category: Maintenance
---

- **Maintenance:** Test-assembly initialization and disposal now have exactly one owner. `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs` holds the immutable `TestServiceContainer`, the repository/fixture paths (`TestRepositoryFixtures`), the shared `WorkspaceIdCache`, and the lazy path-authorized MCP server session, and implements `IAsyncDisposable` — absorbing the hand-rolled disposal that previously lived in `TestBase`. `TestBase`'s 67 mutable `protected static … { get; private set; }` service/path properties became get-only forwarders over that fixture, so a new test service is declared once (in `TestServiceContainer`) instead of three times. Property names and types are unchanged, so all 159 derived test files compile untouched. `InitializeServices()` keeps its `_initLock` once-per-assembly gate and the single `WorkspaceIdCache` verbatim; `AssemblyCleanup.cs` needed no edit.
