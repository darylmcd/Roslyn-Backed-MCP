# analyzer-shadow-loader-lifecycle — Bound analyzer shadow-loader lifetime

**row:** `analyzer-shadow-loader-lifecycle` · **pri:** `High` · **size:** `M` · **deps:** `workspace-validation-error-detail-redaction`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/ValidationIntegrationTests.cs`

## Acceptance

- [ ] Give each workspace explicit ownership of its analyzer-loader lease and shadow root; reload, close, and host disposal release that lease exactly once.
- [ ] Use a collectible/unloadable design or another bounded lifecycle that makes the workspace shadow root safely removable without waiting for process exit.
- [ ] Preserve arbitrary third-party analyzer compatibility: the original analyzer remains unlocked and loaded analyzers/dependencies retain meaningful `Assembly.Location` and adjacent-resource/native-dependency behavior.
- [ ] One Windows lifecycle regression repeats load → analyzer discovery/build → close, proves the load context and shadow tree are reclaimed, then reloads successfully without leaking another tree.

## Evidence

- `AnalyzerReferenceIsolation` creates a non-collectible `AssemblyLoadContext` per loader and path-loads shadow copies, but no loader lease reaches `WorkspaceSession.Dispose`; workspace close therefore cannot unload the context or delete its shadow tree.
- Full Release runs accumulated 245–272 workspace shadow trees, 1,539–1,729 DLLs, and roughly 544–633 MB before process exit. Loading from streams would unlock files but makes `Assembly.Location` empty, which is unsafe for arbitrary public-host analyzers and is not an acceptable shortcut.
