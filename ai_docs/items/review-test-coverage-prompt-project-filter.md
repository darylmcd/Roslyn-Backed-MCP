# review-test-coverage-prompt-project-filter — Apply projectName in the review_test_coverage prompt

**row:** `review-test-coverage-prompt-project-filter` · **pri:** `Medium` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs:203`

## Acceptance

- [ ] With `projectName=RoslynMcp.ShardDiscoveryFixtures` the embedded list contains only that project's tests and the footer count matches.

## Evidence

- P16-F1: 3 fixture tests followed by 47 RoslynMcp.Tests tests, "[Showing 50 of 4446 items]".
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
