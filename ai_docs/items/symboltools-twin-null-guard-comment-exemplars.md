# symboltools-twin-null-guard-comment-exemplars — Align sibling-tool citations in SymbolTools twin null-guard comments

**row:** `symboltools-twin-null-guard-comment-exemplars` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` — the two NotFound-envelope null-guard comment blocks (one near `GetSignatureHelp`, one near the adjacent sibling handler)

## Acceptance

- [ ] Both null-guard comment blocks cite the same canonical set of sibling tools for the NotFound-envelope convention (no `member_hierarchy` vs `symbol_info` divergence).

## Evidence

- Code-quality review of PR #967 (`signature-help-bare-null`) noted the new guard comment cites siblings `symbol_relationships, member_hierarchy` while the adjacent near-identical block cites `symbol_relationships, symbol_info` — a minor reader-confusion nit. — 2026-06-18 backlog-sweep execute.

## Context

Pure comment-alignment cleanup; no behavior change. Both comments accurately describe the same `KeyNotFoundException` → NotFound-envelope convention, they just name different example siblings.
