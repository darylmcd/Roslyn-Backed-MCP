---
category: Maintenance
---

- **Maintenance:** The release-prep `verify-registry-readiness.ps1` `repository-url-matches-name` check no longer false-warns when the MCP server name's repo segment differs from the GitHub repo name. It now compares only the **owner** segment (GitHub auth grants the whole `io.github.<owner>/*` namespace, so `io.github.darylmcd/roslyn-mcp` against repo `Roslyn-Backed-MCP` is valid); a genuinely wrong owner still warns. Closes `registry-readiness-linter-warn-relax`.
