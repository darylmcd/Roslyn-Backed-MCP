### Added

- Added `backlog.d/` fragment pattern for cross-repo audit findings, mirroring the existing `changelog.d/` pattern. `/mcp-server-stress` runs in audited repo X now write actionable findings as fragments at `<X>/backlog.d/<finding-id>.md`; `/backlog-intake` consolidates fragments from configured sibling repos into `ai_docs/backlog.md` and deletes consumed fragments. Replaces the prior cross-repo write-and-copy flow. Fragment schema documented at `ai_docs/items/backlog-d-fragment-schema.md`. Closes `backlog-d-fragment-pattern`.
