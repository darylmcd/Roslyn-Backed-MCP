# compile-check-winforms-generator-dependency — Resolve WinForms source-generator dependencies

**row:** `compile-check-winforms-generator-dependency` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs` — `compile_check` obtains diagnostics from the loaded project compilation.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` — MSBuildWorkspace loads the project and analyzer references.
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs` — add a representative WinForms/WPF project regression.

## Acceptance

- [ ] A restored .NET 10 project with the Windows Forms application-configuration generator returns the same compiler-error verdict in `compile_check` as `dotnet build`; analyzer dependencies load without CS8784.
- [ ] A regression using a representative project fails on the current server with `System.Windows.Forms.Analyzers` missing, then passes after the root dependency-resolution fix.
- [ ] Preserve genuine generator failures as diagnostics; do not suppress CS8784 or omit analyzers.

## Evidence

- SnipCue `D:/SnipCue/SnipCue.slnx` on SDK 10.0.403: Roslyn MCP 5.0.0 `compile_check` returned four CS8784 errors from `ApplicationConfigurationGenerator` because `System.Windows.Forms.Analyzers, Version=10.0.14.0` could not load. The same exact checkout passed gated `dotnet test` (20 focused tests, 0 failed) and its PR #441 hosted CI succeeded. Session transcript contains the full tool response.
