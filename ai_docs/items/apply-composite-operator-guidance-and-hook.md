# apply-composite-operator-guidance-and-hook — Migrate operator guidance and retain alias hook coverage

**row:** `apply-composite-operator-guidance-and-hook` · **pri:** `Low` · **size:** `M` · **deps:** `apply-composite-canonical-alias-surface`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`
- `hooks/hooks.json`
- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`
- `skills/mcp-server-surface-test/prompts/phases/output-and-close.md`

## Acceptance

- [ ] Make operator guidance use the canonical name while recognizing the deprecated alias during the compatibility window.
- [ ] Make the post-apply verification hook match both names.
- [ ] Prove by hook/prompt validation that both invocation names retain the verification reminder.

## Evidence

The safety hook and operator guidance currently recognize only `apply_composite_preview`.

## Context

Depends on `apply-composite-canonical-alias-surface`. Migration child split from `tool-consolidation-adr-and-alias-machinery`.
