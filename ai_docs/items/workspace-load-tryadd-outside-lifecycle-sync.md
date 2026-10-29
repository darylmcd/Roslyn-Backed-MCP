# workspace-load-tryadd-outside-lifecycle-sync — Register loaded sessions under _lifecycleSync honoring removal admission so a load racing ShutdownAsync cannot orphan a session, slot and watcher

**row:** `workspace-load-tryadd-outside-lifecycle-sync` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (LoadAsync TryAdd/Watch ~:478-484)
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] A load that completes after ShutdownAsync/removal admission closes refuses registration and disposes its session, returns the slot and registers no watcher.
- [ ] A concurrent close or eviction between TryAdd and Watch leaves no orphan watcher (compensation today is a no-op in that case).
- [ ] Red-first deterministic race test using the gated-loader double.

## Evidence

- Found by the workspace-load-registration-compensation cold review (pre-existing race) and the gate-release implementer: TryAdd runs outside _lifecycleSync and ignores removal admission.
