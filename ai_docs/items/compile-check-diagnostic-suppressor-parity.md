# compile-check-diagnostic-suppressor-parity - Match compiler diagnostics after analyzer suppression

**row:** `compile-check-diagnostic-suppressor-parity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:161-168`
- (new) `tests/RoslynMcp.Tests/CompileCheckSuppressorParityTests.cs`

## Acceptance

- [ ] Reproduce a compiler diagnostic suppressed by an installed framework DiagnosticSuppressor in a native build, then compare compile_check on that exact project.
- [ ] Honor the compiler diagnostic semantics of applicable registered suppressors in both diagnostic and emit validation modes; retain ordinary unsuppressed compiler failures.
- [ ] Preserve cancellation, unavailable analyzer reporting and compiler-only result scope. Do not add a CS8618/MSTest allowlist or change consumer source to hide the discrepancy.

## Evidence

- 2026-10-22 live installed roslyn-mcp 4.3.0+85b6e3dc559ac05c0bceb8fcbaf390c99a5f31a6 reported three CS8618 errors on injected TestContext properties in tests/RoslynMcp.Tests/NativeCertificateCurrentGrantTests.cs:183, PhysicalGraphStageTests.cs:932 and TestRunnerEvaluationPropertiesTests.cs:1275 in the retained restore worktree. These are observed consumers, not planned edit targets.
- Same-session machine-gated Release rebuild of tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj with --no-restore and -t:Rebuild succeeded: 0 warnings, 0 errors, 22.00s. The full solution incremental Release build also succeeded.
- CompileCheckService reads Compilation.GetDiagnostics or Emit diagnostics directly. Suppressor execution and analyzer availability require explicit investigation; the build/tool discrepancy is observed, the exact suppressor root remains to prove.
- Logs: <user-profile>/.codex/scratch/roslyn-remediate-20261022/restore-rebuild.log and restore-build.log. No test property was changed or diagnostic suppressed by this session.
