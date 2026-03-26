# Add Security Diagnostic Surface To Roslyn MCP Server

## Context

This Roslyn MCP server already exposes `project_diagnostics` and `diagnostic_details` tools that surface compiler diagnostics, analyzer diagnostics, and workspace diagnostics via `CompilationWithAnalyzers`. Any Roslyn-based `DiagnosticAnalyzer` referenced by a project's NuGet packages is automatically picked up.

However, there is no purpose-built surface for **security-focused diagnostics** — an AI agent must call `project_diagnostics`, parse all results, and manually filter for security-relevant IDs. There is also no mechanism to ensure security analyzer packages are present in target projects, and no curated mapping of which diagnostic IDs represent security concerns.

## Objective

Add a security diagnostic surface that enables an AI coding agent to:

1. Check whether a workspace has security analyzers installed
2. Run security-focused analysis and get only security-relevant results
3. Understand what each security finding means and how to fix it
4. Get actionable fix suggestions for common security issues

## What Already Exists (Do Not Duplicate)

- `IDiagnosticService` / `DiagnosticService` — runs `Compilation.GetDiagnostics()` and `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync()` with filtering by project, file, and severity
- `project_diagnostics` tool — exposes the above via MCP
- `diagnostic_details` tool — returns diagnostic description, help link, and available code fixes
- `code_fix_preview` / `code_fix_apply` — previews and applies Roslyn code fixes for diagnostics
- The existing analyzer pipeline picks up any `DiagnosticAnalyzer` from project NuGet package references automatically

## Scope Of Work

### 1. Security Diagnostic ID Registry

Create a static registry that maps known security-relevant diagnostic IDs to categories. This is a curated lookup table, not a new analyzer. It should cover at minimum:

**Microsoft.CodeAnalysis.NetAnalyzers (ships with .NET SDK):**
- `CA2100` — SQL injection
- `CA2109` — Review visible event handlers
- `CA2119` — Seal methods that satisfy private interfaces
- `CA2153` — Avoid handling corrupted state exceptions
- `CA2300`–`CA2330` — Insecure deserialization (BinaryFormatter, JavaScriptSerializer, etc.)
- `CA3001` — SQL injection
- `CA3002` — XSS
- `CA3003` — File path injection
- `CA3004` — Information disclosure
- `CA3005` — LDAP injection
- `CA3006` — Process command injection
- `CA3007` — Open redirect
- `CA3008` — XPath injection
- `CA3009` — XML injection
- `CA3010` — XAML injection
- `CA3011` — DLL injection
- `CA3012` — Regex injection
- `CA3061` — Do not add schema by URL
- `CA3075` — Insecure DTD processing
- `CA3076` — Insecure XSLT script processing
- `CA3077` — Insecure processing in API design
- `CA3147` — Mark verb handlers with ValidateAntiForgeryToken
- `CA5350` — Weak cryptographic algorithm (SHA1)
- `CA5351` — Broken cryptographic algorithm (DES, TripleDES)
- `CA5358`–`CA5404` — Various crypto, TLS, and certificate validation issues

**SecurityCodeScan (if referenced as NuGet package):**
- `SCS0001` — Command injection
- `SCS0002` — SQL injection (LINQ)
- `SCS0003` — XPath injection
- `SCS0005` — Weak random
- `SCS0006` — Weak hash
- `SCS0007` — XML external entity (XXE)
- `SCS0008` — Cookie without HttpOnly
- `SCS0009` — Cookie without Secure
- `SCS0010`–`SCS0039` — CSRF, open redirect, LDAP injection, hardcoded password, certificate validation, etc.

Each entry should carry: diagnostic ID, short name, OWASP category (e.g., "A03:2021 Injection"), severity, and a one-line description.

### 2. New Tool: `security_diagnostics`

Add a new MCP tool that filters the existing diagnostic pipeline to return only security-relevant results.

**Behavior:**
- Calls the existing `IDiagnosticService.GetDiagnosticsAsync()` (do NOT reimplement the diagnostic pipeline)
- Filters results against the security diagnostic ID registry
- Enriches each result with: OWASP category, security severity (critical/high/medium/low), and a fix hint
- Accepts the same `workspaceId`, `project`, and `file` filters as `project_diagnostics`

**Response DTO:**

```csharp
public sealed record SecurityDiagnosticsResultDto(
    IReadOnlyList<SecurityDiagnosticDto> Findings,
    SecurityAnalyzerStatusDto AnalyzerStatus,
    int TotalFindings,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount);

public sealed record SecurityDiagnosticDto(
    string DiagnosticId,         // e.g., "CA3001"
    string Message,
    string SecurityCategory,     // e.g., "Injection"
    string OwaspCategory,        // e.g., "A03:2021 Injection"
    string SecuritySeverity,     // "Critical", "High", "Medium", "Low"
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    string? FixHint,             // Short actionable fix description
    string? HelpLinkUri);

public sealed record SecurityAnalyzerStatusDto(
    bool NetAnalyzersPresent,           // SDK analyzers always present
    bool SecurityCodeScanPresent,       // NuGet package detected
    IReadOnlyList<string> MissingRecommendedPackages);
```

### 3. New Tool: `security_analyzer_status`

A lightweight tool that checks whether security analyzer packages are referenced in the workspace without running a full diagnostic pass.

**Behavior:**
- Iterates `project.AnalyzerReferences` for each project in the workspace
- Checks for known security analyzer assembly names (e.g., `SecurityCodeScan`, `Microsoft.CodeAnalysis.NetAnalyzers`)
- Returns which analyzers are present and which recommended packages are missing
- Suggests NuGet package additions if security coverage is incomplete

### 4. New Prompt: `security_review`

Add an MCP prompt (in `Prompts/RoslynPrompts.cs`) that generates a structured security review prompt for an AI agent. The prompt should:

- Instruct the agent to call `security_analyzer_status` first to verify coverage
- Then call `security_diagnostics` to get findings
- Guide the agent through triaging findings by severity
- Suggest using `code_fix_preview` / `code_fix_apply` for findings that have Roslyn code fixes
- Flag findings that require manual review (no automated fix available)

## Implementation Constraints

- **Do not reimplement the diagnostic pipeline.** The existing `DiagnosticService` already handles compilation, analyzer execution, and diagnostic collection. The new tools filter and enrich those results.
- **Do not add custom DiagnosticAnalyzer implementations.** The security coverage comes from existing NuGet analyzer packages. This feature surfaces and enriches their output, it does not replace them.
- **Follow the existing tool registration pattern.** Use `[McpServerTool]` attributes, inject services via method parameters, use `ToolErrorHandler.ExecuteAsync()`, and return JSON.
- **Follow the existing service pattern.** Add `ISecurityDiagnosticService` in `Core/Services/`, implement in `Roslyn/Services/`, register in `ServiceCollectionExtensions`.
- **Follow the existing DTO pattern.** All new types go in `Core/Models/`. No raw Roslyn types in DTOs.
- **The security ID registry should be a static class**, not configuration-driven. It is a curated security knowledge base that ships with the server. It can be extended over time but does not need runtime configuration.
- **Mark new tools as stable** (`ReadOnly = true, Destructive = false, Idempotent = true`) — they are read-only analysis tools.

## Testing

Add integration tests in `tests/RoslynMcp.Tests/` following the existing test patterns:

- `SecurityDiagnosticIntegrationTests.cs`
  - Load a workspace with known security issues (e.g., `string.Format` in SQL query construction)
  - Verify `security_diagnostics` returns the expected finding with correct OWASP category
  - Verify `security_analyzer_status` correctly detects present/missing analyzer packages
  - Verify filtering by project and file works
  - Verify the response contains zero findings for a clean project

Create a small test fixture project under `tests/fixtures/SecurityTestProject/` with intentional security anti-patterns for the tests to detect.

## File Locations

| File | Purpose |
|------|---------|
| `src/RoslynMcp.Core/Models/SecurityDiagnosticDto.cs` | Security diagnostic DTOs |
| `src/RoslynMcp.Core/Services/ISecurityDiagnosticService.cs` | Service interface |
| `src/RoslynMcp.Roslyn/Services/SecurityDiagnosticService.cs` | Service implementation |
| `src/RoslynMcp.Roslyn/Services/SecurityDiagnosticRegistry.cs` | Static ID → category mapping |
| `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs` | MCP tool definitions |
| `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs` | Add `security_review` prompt |
| `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` | Register new service |
| `tests/RoslynMcp.Tests/SecurityDiagnosticIntegrationTests.cs` | Integration tests |
| `tests/fixtures/SecurityTestProject/` | Test fixture with security anti-patterns |

## Success Criteria

1. An AI agent can call `security_analyzer_status` to check if a workspace has adequate security analyzer coverage
2. An AI agent can call `security_diagnostics` to get only security-relevant findings with OWASP categorization and fix hints
3. The tools work with any .NET project without requiring the project to add special configuration — SDK analyzers are always available
4. The `security_review` prompt guides an agent through a complete security review workflow using the existing code fix tools
5. All new tools follow the server's established patterns for registration, error handling, concurrency, and serialization
