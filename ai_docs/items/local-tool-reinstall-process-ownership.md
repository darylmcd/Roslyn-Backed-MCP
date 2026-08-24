# local-tool-reinstall-process-ownership — Scope reinstall shutdown to the owned process

**row:** `local-tool-reinstall-process-ownership` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj`
- `eng/reinstall-local-tool.ps1` (new)
- `tests/RoslynMcp.Tests/LocalToolReinstallProcessOwnershipTests.cs` (new)

## Acceptance

- [ ] Replace opt-in `taskkill /F /IM roslynmcp.exe` with ownership-scoped shutdown that targets only the process associated with the local reinstall operation.
- [ ] Preserve bounded cleanup and actionable failure diagnostics on Windows without adding image-wide or name-wide termination on other hosts.
- [ ] Prove a controlled owned process is stopped before reinstall while a separate unrelated `roslynmcp` process survives.
- [ ] Keep reinstall opt-in and avoid introducing a service, global lock, or credential-bearing process registry.

## Evidence

The current opt-in reinstall target kills every Windows process named `roslynmcp.exe`. That can terminate unrelated MCP sessions and has no ownership proof, so the destructive boundary is broader than the requested reinstall operation.
