---
category: Added
---

- **Added:** `/roslyn-mcp:audit-deep` plugin skill — the comprehensive Roslyn MCP server audit + experimental-promotion scorecard + plugin-skill audit, now shipped with the plugin instead of relying on each consuming repo to keep `ai_docs/prompts/deep-review-and-refactor.md` current. Three modes: `full`, `promotion-only`, `read-only`. Skill requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts with a clear message when absent rather than running a non-MCP fallback. Phase 6 mutations confined to disposable worktrees the prompt creates (read-only against the audited repo's main). Closes (split A) `audit-deep-skill-migration` — paired with the archive-and-surface-audit-integration follow-on.
