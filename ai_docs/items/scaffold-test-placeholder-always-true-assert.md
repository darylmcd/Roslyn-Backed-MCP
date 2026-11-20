# scaffold-test-placeholder-always-true-assert — Stop scaffolding always-true assertions

**row:** `scaffold-test-placeholder-always-true-assert` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Scaffolded placeholders for static-method and no-method targets raise no MSTest analyzer diagnostic at Error severity (e.g. Assert.Inconclusive with a message).
- [ ] Regression asserts the scaffolded file is analyzer-clean, not just compiler-clean.
- [ ] Probe the xUnit and NUnit templates for the same construct.

## Evidence

- P12-F1: 2 analyzer errors MSTEST0032 after applying a 3-target batch; compile_check stayed green.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
