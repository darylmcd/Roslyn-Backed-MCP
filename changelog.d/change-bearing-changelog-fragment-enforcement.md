---
category: Maintenance
---

- **Maintenance:** Change-bearing branches now require a changed, valid changelog fragment through the authoritative release gate, while a repo-local Codex hook supplies early commit, push, PR, and ship-preflight feedback. Internal planning-only work remains exempt and assembled releases are limited to consumed fragments plus all six canonical version files. Closes `changelog-fragment-category-body-parity`, `change-bearing-changelog-fragment-enforcement`, and `codex-pre-publish-changelog-hook`.
