# editorconfig-held-source-write

**row:** `editorconfig-held-source-write` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/EditorConfigFileTransaction.cs`

## Acceptance

- [ ] Exercise an actual coordinator callback replacing the admitted config coordinate with an alias to identical foreign prebytes.
- [ ] Refuse before foreign mutation; preserve exact original bytes, undo state, recovery and watcher attribution.
- [ ] Reuse the held no-follow source transition persistence contract for existing and newly created config entries.

## Evidence

- Cold full-diff review traced RunOwnedWrite path resolution into a raw FileStream after mutation admission; late publication refusal cannot authorize an earlier foreign write.
