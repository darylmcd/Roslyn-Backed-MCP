| Field | Content |
|---|---|
| Route | deepen |
| Diagnosis | `Directory.Packages.props:31` pins `coverlet.collector` 10.0.1, retaining the upstream `CoverletInProcDataCollector.GetInstrumentationClass` teardown scan implicated by the row. Coverlet PR #1987 removes that scan in `src/legacy/coverlet.collector/InProcDataCollection/CoverletInProcDataCollector.cs` and registers tracker unload delegates; its merge commit is an ancestor of v10.1.0. The release-note headline names the associated ProcessExit race, but the PR diff also contains the requested scan replacement and atomic hit-file writes. `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj:21` consumes the central pin. |
| Approach | Update `Directory.Packages.props:31` to stable `coverlet.collector` 10.1.0. Keep the existing `eng/verify-release.ps1:503-504` VSTest collection path; verify the upgraded binary through the real Windows coverage gate. |
| Scope | 1 production/config file: `Directory.Packages.props`; 0 test files; 1 doc file; no deletions. The orchestrator owns the changelog fragment and backlog closure. Fragment: `changelog.d/coverlet-net10-session-end-crash-upgrade.md`. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | `just ci` skips coverage, so it cannot establish this row's acceptance. Compare the Windows Application Event Log only within the verification run window and inspect exit status plus nonempty, parseable Cobertura. A clean run supports this environment; it cannot prove all Windows/.NET 10 hosts. No refactor-shaped fanout applies. |
| Validation | Run `just ci` and `pwsh -NoProfile -File ./eng/verify-release.ps1 -Configuration Release` on Windows without `-NoCoverage`; require successful process exit, valid nonempty `coverage.cobertura.xml`, and no matching `testhost.exe` access violation (Event ID 1000 / code 0xc0000005) in the run window. Record start/end timestamps and inspect VSTest/TRX diagnostics if red; do not retry away a failure. |
| Performance review | N/A — dependency correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Upgrade Coverlet collector to 10.1.0 to avoid the Windows .NET 10 coverage teardown crash. |
| Backlog sync | Close rows: [coverlet-net10-session-end-crash-upgrade]. Mark obsolete: []. |
