---
category: Fixed
---

- **Fixed:** Split `skills/mcp-server-surface-test/prompts/full.md` (96 KB, exceeds Read tool token cap) into a slim orchestrator + three phase-group sub-files under `prompts/phases/`. Each sub-file is well under the 25K-token cap. The orchestrator retains the `full.md` filename for backward compatibility. Added `Skill_PromptFiles_BelowReadTokenCap` test asserting all prompt files stay ≤ 100 KB.
