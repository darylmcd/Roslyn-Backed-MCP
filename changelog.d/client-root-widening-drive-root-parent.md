---
category: Changed
---

- **Changed:** With `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` and `expandSanctionedRoots=true`, a sanctioned root directly under a drive root (`D:\repo`, `/repo`) now reaches sibling worktrees named `<repo>-<suffix>` (`D:\repo-audit-x`) at load and apply time; the whole drive, `D:\repo2` and unrelated siblings stay rejected. Previously such roots never widened. Migration note: see ADR 0002 addendum. Advances `client-root-widening-drive-root-parent`.
