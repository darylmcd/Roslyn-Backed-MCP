---
category: Fixed
---
- **Fixed:** Physical file identity now follows the platform (`FileSystemPath`) in composite apply/undo snapshots, revert-by-sequence overlap checks, workspace drift dedupe, and replace-invocation / parameter-object callsite counts (rekeyed by `DocumentId`), so case-distinct files stay distinct on case-sensitive filesystems. Closes `composite-apply-platform-path-identity` and `workspace-drift-platform-path-identity`; `change-signature-callsite-update-path-identity` premise already fixed by #1779, remaining instances fixed here.
