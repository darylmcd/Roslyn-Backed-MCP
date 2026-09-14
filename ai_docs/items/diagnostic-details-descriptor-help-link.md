# diagnostic-details-descriptor-help-link

**row:** `diagnostic-details-descriptor-help-link` · **pri:** `Low` · **size:** `S`

## Anchors

- src/RoslynMcp.Roslyn/Services/DiagnosticService.cs
- tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs

## Acceptance

- [ ] Honor DiagnosticDescriptor.HelpLinkUri for analyzer and generator diagnostics; retain a compiler fallback only for compiler IDs and pin absent-link behavior.

## Evidence

BuildHelpLink currently formats every ID as a learn.microsoft.com C# compiler-message URL, including MCP002 and CA/IDE rules.
Observed during diagnostic reliability remediation on 2026-09-14.
