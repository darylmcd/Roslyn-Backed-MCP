# workspace-id-omitted-residual-recovery-coherence — residual-case elicitation vs schemaHint coherence for flipped tools

**row:** `workspace-id-omitted-residual-recovery-coherence` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (`TryAutoLoadWorkspaceAsync` None branch ~604, `IsWorkspaceIdRecoveryAllowedFor` ~457, pre-dispatch comment ~187)

## Acceptance

- [ ] Intent decided: EITHER decouple `IsWorkspaceIdRecoveryAllowedFor` from the `Required` flag (mirroring how order-1 deliberately decoupled `IsWorkspaceIdAutoResolveAllowedFor`) so elicitation recovery survives the optional-flip, OR correct the comments to state the residual path terminates at the schemaHint envelope for optional tools
- [ ] Residual-case test for a flipped tool pinning the chosen behavior (elicitation fires, or schemaHint envelope returned). ≤1 prod file, ≤1 test file

## Evidence

- Batch-level cold review of the composed feature, 2026-06-09 backlog-sweep execute Step 13.

## Context

Cross-initiative seam left by the workspace-auto-load sweep (PRs #957/#958/#959). `TryAutoLoadWorkspaceAsync`'s `DiscoveryStatus.None` branch + its doc (~lines 187, 604-605) state the residual case (omit `workspaceId` + 0 loaded + nothing discoverable) "falls through to the binder/elicitation path (the elicitation fallback when the client supports it)". But order-3 flipped `go_to_definition`/`find_references`/`document_symbols` to `Required:false`, and `IsWorkspaceIdRecoveryAllowedFor` is **Required-gated** (`schema is { Type: "string", Required: true }`), so for exactly these 3 flipped tools the catch-path elicitation recovery no longer fires — an elicitation-capable client that previously got an interactive "provide a workspace path" prompt now gets the static `schemaHint`/`RequireResolvedWorkspaceId` envelope. Functionally safe (clear actionable error → `workspace_load`), but a stale comment + silent UX downgrade contradicting the stated design.
