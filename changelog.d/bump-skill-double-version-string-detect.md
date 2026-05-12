---
category: Maintenance
---

- **Maintenance:** `/bump` skill now runs a per-file occurrence preflight before editing version files; files with >1 occurrence of the old version string use `replace_all: true` automatically, and files where the version string is absent abort with an explicit error rather than silently failing. Prevents the "Found 2 matches of the string to replace" Edit error against `.claude-plugin/server.json`.
