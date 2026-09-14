# diagnostic-details-repo-provider-load-failures

**row:** `diagnostic-details-repo-provider-load-failures` · **pri:** `Medium` · **size:** `M`

## Anchors

- src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs
- src/RoslynMcp.Roslyn/Services/CSharpFeatureProviderLoader.cs
- tests/RoslynMcp.Tests/CodeFixProviderRegistryTests.cs
- tests/RoslynMcp.Tests/CSharpFeatureProviderLoaderTests.cs

## Acceptance

- [ ] Reproduce the standalone built-host MCP002 detail lookup against this repository and identify which provider loading stage produces 18 failures using secret-safe operator diagnostics.
- [ ] Fix the demonstrated dependency/loading classification cause within the anchored provider-loading boundary, or record concrete environment remediation when the failure is not a product defect; pin the one reproduced failure shape.
- [ ] Keep real provider failures visible with fixEnumerationComplete=false and failedProviderCount; do not convert failure to a successful empty provider result.

## Evidence

On 2026-09-14, a fresh Debug stdio host loaded RoslynMcp.slnx and project_diagnostics(projectName=RoslynMcp.Tests, diagnosticId=MCP002) emitted ToolCallErrorWireContractTests.cs:350:30. After the location lookup fix, diagnostic_details resolved that exact diagnostic but returned supportedFixes=[], fixEnumerationComplete=false, failedProviderCount=18. Observed twice in standalone-host verification; underlying loader cause not yet established. The live user-installed server was not changed.
