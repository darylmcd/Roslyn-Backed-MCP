---
category: Fixed
---

- **Fixed:** a time-of-check/time-of-use hole on the preview-token apply path: redeeming a preview token now revalidates every changed document path against the configured sanctioned-root boundary under the write gate, instead of trusting the boundary check performed minutes earlier at preview time. `apply_multi_file_edit` now writes to the canonical link-resolved target returned by path validation rather than to the client-supplied path string. Workspaces admitted via `workspace_load(expandSanctionedRoots: true)` are revalidated against that same one-level-widened boundary, so sibling-worktree applies keep working.
