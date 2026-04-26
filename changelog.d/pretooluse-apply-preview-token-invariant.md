---
category: Fixed
---

- **Fixed:** Removed the transcript-scanning PreToolUse gate for Roslyn apply tools, relying on required `previewToken` inputs and tool-level preview-store validation instead. Closes `pretooluse-apply-preview-token-invariant`.
