# preview-token-stale-envelope-cause-text — Other PreviewTokenStaleException sites report the wrong cause

**row:** `preview-token-stale-envelope-cause-text` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` (fixed "workspace was reloaded" text for every stale-token throw except `PreviewSourceChangedException`)
- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs` (session-changed refusal)
- (new) `tests/RoslynMcp.Tests/ToolErrorHandlerStaleTokenTests.cs`

## Acceptance

- [ ] Red first: each non-reload `PreviewTokenStaleException` throw site (CompositePreviewStore session change, ToolDispatch, ApplyWithVerifyTool, WorkspaceForkApplyService) produces an envelope naming its real cause, asserted by envelope text.
- [ ] Reload-caused throws keep the current text.

## Evidence

- PR #2015 implementer sighting: the new `PreviewSourceChangedException` subtype fixed one site; the others still tell the user the workspace was reloaded. Acknowledged in ADR 0033.
