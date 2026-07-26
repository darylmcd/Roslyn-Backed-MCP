# workspace-manager-event-subscriber-isolation — Isolate workspace lifecycle subscribers

**row:** `workspace-manager-event-subscriber-isolation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/WorkspaceReloadedEventTests.cs`

## Acceptance

- [ ] `WorkspaceClosed` and `WorkspaceReloaded` invoke each subscriber independently.
- [ ] One throwing subscriber is logged but cannot prevent later subscribers from receiving the lifecycle event.
- [ ] Regressions cover both event paths with a throwing first subscriber and an observing later subscriber.

## Evidence

- Cold review on 2026-07-26 found each multicast delegate is invoked once inside one try/catch, so the first throwing subscriber suppresses every later cache or lifecycle subscriber.
