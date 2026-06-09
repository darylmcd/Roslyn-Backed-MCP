---
category: Added
---

- **Added:** workspace-id auto-resolution in the read-path middleware (`StructuredCallToolFilter`) — a read-only, non-destructive tool called with `workspaceId` omitted/empty now resolves to the single loaded workspace, or fast-fails with a structured error listing the loaded ids when two or more are loaded. New `_meta.autoResolution` field (`explicit` | `single-workspace` | `fast-fail`) makes adoption measurable. No schema change; explicit-id callers are unaffected. Closes `workspace-id-omitted-single-resolve`.
