---
category: Fixed
---

- **Fixed:** Local global-tool reinstall now stops only an explicitly identified `roslynmcp` process, verifies its PID and start time before bounded termination, preserves unrelated sessions, and fails closed on real uninstall errors. Closes `local-tool-reinstall-process-ownership`.
