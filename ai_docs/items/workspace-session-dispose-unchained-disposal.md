# workspace-session-dispose-unchained-disposal — Chain workspace and lease disposal

**row:** `workspace-session-dispose-unchained-disposal` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1877` (`WorkspaceSession.Dispose`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:129` (load cleanup)
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:141` (cleanup warning path)
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] Workspace, analyzer lease, and session load lock disposal are all attempted when an earlier step throws; preserve ordered failures and the primary error.
- [ ] The two loader workspace-to-lease disposal paths use the same failure-safe cleanup mechanism, retaining the original load exception and reporting cleanup failures.
- [ ] A red-first injectable cleanup seam observes later steps after throwing workspace and lease disposals; cover the session three-step and loader two-step callers with focused integration tests.
- [ ] Clear the session workspace reference before lease disposal; do not capture an `MSBuildWorkspace` in a delegate that stays rooted during lease cleanup retries.

## Evidence

- Cold plan review on 2026-10-31 found sequential disposal in `WorkspaceSession.Dispose` and both `WorkspaceSessionLoader` paths. A workspace-disposal throw skips the lease (and session lock). The loader's separate admission-execution cleanup may mask a primary failure and needs its own bounded decision.
