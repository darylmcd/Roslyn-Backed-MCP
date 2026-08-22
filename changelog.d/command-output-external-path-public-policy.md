---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Public build and test command diagnostics no longer retain arbitrary absolute paths emitted only by child processes. Windows drive, UNC, and POSIX paths inside the command workspace become `/`-normalized relative paths; other absolute paths become `<external-path>`. The policy now covers stdout, stderr, early-kill reasons, failure-envelope summaries/tails, and paginated test failure messages/stack traces while preserving compiler `(line,column)` and stack `:line N` suffixes. Internal execution records remain full fidelity. **Migration:** stop parsing absolute paths from diagnostic text; navigate with retained workspace-relative locations and treat `<external-path>` as an opaque external-file marker.
