# apply-with-verify-complete-diagnostic-baseline — Compare complete diagnostic baselines

**row:** `apply-with-verify-complete-diagnostic-baseline` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`

## Acceptance

- [ ] Apply verification compares a complete error-identity baseline rather than only the default first diagnostic page.
- [ ] The change does not expand the public `compile_check` response or add an extra compile pass.
- [ ] A regression with more than the default diagnostic limit detects a newly introduced error beyond the first page.

## Evidence

- Cold review found that introduced-error subtraction consumes the default capped diagnostic set.
## Validation

- Extend `tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs` with the beyond-default-limit regression and keep `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs` green.
