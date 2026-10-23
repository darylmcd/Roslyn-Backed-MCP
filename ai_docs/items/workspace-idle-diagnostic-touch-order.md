# workspace-idle-diagnostic-touch-order

**row:** `workspace-idle-diagnostic-touch-order` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1569`

## Acceptance

- [ ] Compute previous idle duration before TouchAccess while retaining the access update.
- [ ] Prove the greater-than-sixty-minute diagnostic is reachable with a controlled clock and normal reads remain current.

## Evidence

- Independent full-diff review found TouchAccess immediately precedes the LastAccessedUtc idle-duration calculation, making the idle diagnostic unreachable.
