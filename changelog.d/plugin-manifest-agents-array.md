---
category: Fixed
---

- **Fixed:** `/plugin install roslyn-mcp@roslyn-mcp-marketplace` failed manifest validation (`agents: Invalid string: must end with ".md"`) because `.claude-plugin/plugin.json` declared `agents` as the directory `./agents/`; it now lists the agent files explicitly, and a contract test keeps the list in sync with `agents/*.md`. Closes #1795.
