---
category: Maintenance
---

- **Maintenance:** `/backlog-sweep:execute` Step 2 defense-in-depth now also queries `gh pr list` for open contributor PRs touching the initiative's anchor files — initiatives with file collisions updated within the past 14 days are marked `obsolete` before claim, closing the race where a contributor opens a PR for the same file set between plan generation and execution without using the Reserved-row marker.
