# workspace-close-isolated-nuget-handles — Investigate private NuGet cache retention after workspace close

**row:** `workspace-close-isolated-nuget-handles` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:524` CloseCore lifecycle investigation boundary.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1619` WorkspaceSession.Dispose ownership boundary.
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` workspace_close lifecycle contract.
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs` isolated workspace lifecycle regression location.
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs` analyzer ownership regression location, only if traced cause reaches this mechanism.

## Acceptance

- [ ] Reproduce a private NuGet-cache deletion failure with a workspace created by the probe; capture exact retained files, handles and owning managed/native metadata or assembly load path before classifying a code bug.
- [ ] Keep the server alive; close only the probe's owned workspace. Distinguish another active workspace's legitimate references from released-workspace retention.
- [ ] If the defect is confirmed, repair every close/reload/eviction/host-disposal instance of the traced ownership mechanism; record old-behavior failing Windows filesystem regression and fixed passing result.
- [ ] If the claim does not reproduce on current source, record the inspected reproduction and disposition; do not invent a missing Dispose call or infer causation from a PID name.
- [ ] Do not kill the server, weaken deletion assertions or increase retry windows to hide confirmed retention.

## Evidence

- Re-vet base: `c43e8fa6995151b22be6feb2c4a1020263b88ce5`. Original filing: PR #1744, immutable head `d94fcf56fa2fa72ab98440dfa37b2e32233f6f12`.
- Current `WorkspaceManager.cs:540-542` reads `_fileWatcher.Unwatch(workspaceId);`, `_previewStore.InvalidateAll(workspaceId);`, `session.Dispose();`.
- Current `WorkspaceManager.cs:1621,1628-1630` reads `Workspace?.Dispose();`, `Workspace = null;`, `AnalyzerLease?.Dispose();`, `LoadLock.Dispose();`.
- PR #1744 reports SnipCue bl-0515 EPERM with PID 48528 and bl-0519 EPERM with PIDs 5256/2412 after workspace count zero. These are historical claims from the immutable detail, not reproduction generated in this re-vet; exact owning-handle evidence and retained receipt paths were not established here.
- No defective ownership construct was traced in this read-only re-vet. Import as `[type: chore]` investigate-first; do not retain the original bug classification as proven.
- Pre-import main at the recorded re-vet base had no equivalent NuGet-handle-retention row. Existing workspace-close case-comparison/PID-reuse rows concern process-drain identity and are different mechanisms.

- Current-session 2026-10-07 reproduction boundary: canonical type-extraction cache reclamation after PR1758 merge returns `scratchRemoved:false`, `EPERM`, and `holder unknown: Restart Manager probe exited 1` at 05:01Z. Own workspace close used `drainProcesses:false`; no task-rooted worker was found; no machine-wide shutdown or holder kill was issued.
- Retained files inspected under `<private-scratch-root>/a177c9f2-type-extraction-argument-refusals-public-message/nuget/microsoft.extensions.logging.abstractions/10.0.12/`: package archive and `analyzers/dotnet/roslyn4.4/cs/Microsoft.Extensions.Logging.Generators.dll` plus resource assemblies. Presence does not prove which file is locked or who owns the handle; preserve investigate-first classification.

Current-session 2026-10-07 suppression landing PR1760 at 05:54Z also returned scratchRemoved:false and EPERM for <private-scratch-root>/a177c9f2-suppression-argument-refusals-public-message; holder unknown because Restart Manager probe exited1. Owned executor and cold-review workspaces closed without process draining; native task-rooted process audits were empty; investigate exact handle ownership before classifying a lifecycle defect.

Recovery observation 2026-10-07: later canonical extraction cache reclaim reports scratchRemoved:true; exact extraction cache and quarantine marker are directly absent. Same-session suppression reclaim still reports EPERM/unknown holder; no owning handle or lifecycle root cause is inferred from this different retention duration.

2026-10-09 evidence: navigation PR1764 full producer passed/reaped and owned semantic workspaces closed with drainProcesses:false, but canonical post-merge scratch reclaim failed EPERM under bsweep-scratch/a177c9f2-navigation-and-locator-argument-refusals-public-message. Quarantine marker at <user>/AppData/Local/Temp/bsweep-scratch/.quarantine/a177c9f2-navigation-and-locator-argument-refusals-public-message.json. Restart Manager diagnostic was only CLIXML framing; holder and root cause remain unconfirmed. Investigate owning handles without globally draining peer build servers.

2026-10-09: lifecycle PR #1766 merged after all owned workspaces closed with drainProcesses:false and recorded executor processes exited. Canonical land still quarantined a177c9f2-workspace-lifecycle-argument-refusals-public-message scratch after EPERM; Restart Manager probe exited 1 with only CLIXML framing. Underlying holder/root cause remains unconfirmed. Existing global row bsweep-holder-probe-clixml-diagnostic (claude-config PR #733) owns actionable diagnostic repair. Do not kill peer processes or infer NuGet ownership from this evidence.
