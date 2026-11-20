# find-implementations-abstract-members-empty — Return implementations for abstract members and abstract classes

**row:** `find-implementations-abstract-members-empty` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`

## Acceptance

- [ ] `find_implementations` at `src/RoslynMcp.Roslyn/Services/ForkAdmissionTask.cs` 52:37 and for `RoslynMcp.Roslyn.Services.ForkAdmissionTask` returns the 3 derived implementations, as the description states ("interface or abstract member").

## Evidence

- G2-F1: 3/3 calls returned count 0.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
