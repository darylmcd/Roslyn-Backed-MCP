# workspace-msbuild-native-diagnostic-log-payload — Qualify and bound native MSBuild diagnostic log text

**row:** `workspace-msbuild-native-diagnostic-log-payload` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs:29-53`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceReloadGlobalPropertiesTests.cs`

## Acceptance

- [ ] Use a synthetic property-expanded project diagnostic to qualify rendered and structured log output. If values propagate, remove the raw payload from operational logs while retaining safe diagnostic identity and the established workspace diagnostic contract.

## Evidence

- Source review at `34a87d9c`. The workspace failure handler logs the native message verbatim. Property propagation through that message is a source-derived possibility and has no runtime witness yet.
