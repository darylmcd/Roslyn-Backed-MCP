---
category: Fixed
---

- **Fixed:** Malformed `workspace_load` requests that omit `path` while supplying an unrecognized argument now return `InvalidArgument` with the canonical `path` schema hint before opening a user form. Valid missing-path recovery remains available across both supported protocol eras. Bootstrap guidance now includes the exact load argument and distinguishes loading from analyzer readiness. Closes `workspace-load-misnamed-path-elicitation`.
