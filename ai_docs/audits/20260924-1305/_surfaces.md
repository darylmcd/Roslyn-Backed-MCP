# Surfaces — run 20260924-1305

| Server id | Version | Stack | Launch | Transport | Profiles | Pin |
|---|---|---|---|---|---|---|
| roslyn (registered) | 4.2.1+4eba2601 | C#/.NET 10, MCP SDK 2.2.0 | `dotnet exec … dnx Darylmcd.RoslynMcp@4.2.1 --source https://api.nuget.org/v3/index.json` → `<user>/.nuget/packages/darylmcd.roslynmcp/4.2.1/tools/net10.0/any/RoslynMcp.Host.Stdio.dll` | stdio | 1 | **60 commits behind HEAD e24a48a4** — flagged |
| roslyn (raw harness, installed) | 4.2.1+4eba2601 | same | `dotnet <user>/.nuget/packages/darylmcd.roslynmcp/4.2.1/tools/net10.0/any/RoslynMcp.Host.Stdio.dll` | stdio | 1 | same binary as registered |
| roslyn (raw harness, HEAD) | 4.2.1+e24a48a4 | same | `<repo>/.worktrees/surface-test-20260924T130517Z-ro/src/RoslynMcp.Host.Stdio/bin/Debug/net10.0/RoslynMcp.Host.Stdio.exe` (built at HEAD) | stdio | 1 | HEAD |

Registered-server sanctioned roots: 1 (plugin env). Raw harness: ROSLYNMCP_SANCTIONED_ROOTS unset (probe O1), then set to the disposable copy.
