---
category: Maintenance
---

- **Maintenance:** spike note `ai_docs/items/sampling-spike.md` evaluated server-initiated MCP `sampling/createMessage` against three candidate tool flows (auto-XML-doc generation, refactor-summary text from `workspace_changes`, auto-test-name generation). One `go` verdict (auto-test-name generation, which collapses N rename loops to 1 sampled call per scaffold); two `no-go` (auto-XML-doc, refactor-summary — both context-starved relative to the agent's outer loop). Per-candidate follow-up row filed for the `go` verdict only; no tool body changes in this PR. Closes `sampling-driven-tool-flows-spike`.
