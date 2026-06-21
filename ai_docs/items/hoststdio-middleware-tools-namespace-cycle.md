# hoststdio-middleware-tools-namespace-cycle — break the Middleware ↔ Tools namespace cycle

**row:** `hoststdio-middleware-tools-namespace-cycle` · **pri:** `Low` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`

## Acceptance

- [ ] One direction broken — shared contract extracted into a third namespace, or inverted via an interface
- [ ] Regression: an architecture/namespace-dependency test asserts no Middleware↔Tools cycle

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md`; re-confirmed at HEAD 6acab28. Source: 2026-05-31 surface-test, Standing Directive #3.

## Context

[repo-code, not an MCP-surface defect — verified at HEAD 6acab28] `get_namespace_dependencies` reported a circular namespace dependency `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools`; confirmed live — `Middleware/StructuredCallToolFilter.cs` references the Tools namespace and `Tools/SymbolTools.cs` references the Middleware namespace. A namespace cycle is a layering smell that complicates extraction/testing.

## Re-plan note (2026-06-21 backlog-sweep — DEFERRED, premise corrected)

The 2026-06-20 sweep deepener's plan assumed `StructuredCallToolFilter` imports `RoslynMcp.Host.Stdio.Tools` **solely** for `WorkspaceTools.ResolveOptionalWorkspaceId`. That is **false** (verified by a Release build): the filter ALSO uses `ToolErrorHandler` (namespace `RoslynMcp.Host.Stdio.Tools`, `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`) at 3 live call sites (`ClassifyAndFormat`, `InjectMetaIfPossible` ×2). Removing the `using RoslynMcp.Host.Stdio.Tools;` fails with 3× CS0103 on `ToolErrorHandler`. Breaking the Middleware→Tools edge therefore requires **first relocating `ToolErrorHandler`** out of the `Tools` namespace — it is referenced via `ToolErrorHandler.` in ~8 source files (`StructuredCallToolFilter.cs`, `ValidationBundleTools.cs`, `ValidationTools.cs`, `PromptShimTools.cs`, `ServerResources.cs`, `WorkspaceResources.cs`, `Program.cs`, and `RoslynMcp.Roslyn/Services/WorkspaceManager.cs`) plus tests — well beyond a single ≤4-file initiative.

**Split before re-planning (→ two initiatives):** (a) move `ToolErrorHandler` from `RoslynMcp.Host.Stdio.Tools` into a shared namespace (e.g. `Catalog`) + fix the ~8 consumer usings; THEN (b) move `ResolveOptionalWorkspaceId` to `Catalog/WorkspaceResolutionHelper.cs`, drop the filter's `using ...Tools;`, and add the reflection arch test. Note: `ResolveOptionalWorkspaceId` has exactly 3 live call sites (WorkspaceTools ×2, filter ×1) + 1 `<see cref>` — NOT the 4 the original plan claimed. `size: L` reflects the ToolErrorHandler relocation blast radius; split into (a)/(b) before the next sweep picks this up. Source: 2026-06-21 backlog-sweep execute Step 5a (PR not opened; worktree reverted clean).
