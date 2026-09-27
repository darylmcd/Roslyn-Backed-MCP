---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `WindowsPathResourceTests`, `WorkspaceCachePrewarmTests`, and `WorkspaceForkApplyTests` — each class only reads the shared sample workspace through the synchronized `WorkspaceIdCache` or works on its own isolated workspace copy (fork, restore, and close scoped to that copy), so the opt-out was removed from all three, proven safe by repeated concurrent test runs.
