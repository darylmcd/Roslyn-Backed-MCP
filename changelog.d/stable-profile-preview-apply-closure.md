---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `ROSLYNMCP_TOOL_TIERS=stable` now exposes 94 callable tools and omits 19 previews whose only token-redemption route is experimental; the default `stable,experimental` surface is unchanged. **Migration:** stable-only clients must rediscover with `tools/list` and use only returned tools; enable `stable,experimental` when move/extract/range refactoring, file lifecycle, dead-code removal, project mutation, or test-scaffolding previews are required. Closes `stable-profile-preview-apply-closure`.
