# automatic-reload-analyzer-reader-lifetime

**row:** `automatic-reload-analyzer-reader-lifetime` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Helpers/SourceGeneratorCompilation.cs`

## Acceptance

- [ ] Make automatic stale reload obtain the existing per-workspace writer exclusion without reentrant deadlock.
- [ ] Hold an actual generator/provider reader across another stale invocation; prove late dependency resolution remains valid until the reader releases.
- [ ] Preserve explicit reload, close and eviction exclusion and verify exact current-source regression red then green.

## Evidence

- Cold full-diff review found ApplyStalenessPolicyAsync invokes ReloadAsync before acquiring its reader/writer lock; old analyzer leases can retire while another reader still uses them.
