# code-action-provider-execution-failure-redaction-coverage — Pin provider execution diagnostics

**row:** `code-action-provider-execution-failure-redaction-coverage` · **pri:** `Low` · **size:** `S` · **deps:** `code-action-provider-loading-consolidation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`
- `tests/RoslynMcp.Tests/CodeActionServiceTests.cs`

## Acceptance

- [ ] Inject deterministic throwing code-fix and refactoring providers without reflection or private-field mutation.
- [ ] Assert both paths emit a stable category, exception type, and correlation id without a raw exception object, message, path, or user source.
- [ ] Assert cancellation still propagates unchanged.

## Evidence

The 2026-08-28 provider-loader remediation found both execution catches passing raw exception objects to `ILogger`. The production path now projects secret-safe diagnostics, but no direct regression can force either provider failure through `CodeActionService`.
