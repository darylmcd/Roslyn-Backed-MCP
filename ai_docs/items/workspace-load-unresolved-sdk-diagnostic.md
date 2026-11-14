# workspace-load-unresolved-sdk-diagnostic — Surface an unavailable workspace SDK

**row:** `workspace-load-unresolved-sdk-diagnostic` · **pri:** `Medium` · **size:** `M` · **verified:** `b523e0c4562eb6dd376f850706f35dabfde766e1`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:113` — physical project load.
- `global.json` — exact SDK resolution is part of the workspace contract.

## Acceptance

- [ ] When the workspace pins an SDK absent from the server process, load and stale reload expose the requested SDK and a corrective diagnostic. Preserve the underlying exception in internal diagnostic evidence.

## Evidence

- SnipCue SHA bafcaeb after SDK 11.0.100 pin: heartbeat reported Roslyn 5.1.1 live. Loading the concrete solution and unit project with autoRestore=false returned "InvalidOperationException: The operation is not valid in the current state. Check the tool contract and retry." A previously cached workspace reloaded twice with opaque InternalError. Correlation IDs df2fd723448a4c849169d75a892b8e1f and 7de9d1e91a124b18a43c304e4fbf037b.
- From the same checkout the default dotnet reports "A compatible .NET SDK was not found" and "Requested SDK version: 11.0.100". The scoped SDK installation successfully built and tested the same project. Missing SDK is the suspected trigger. The server exception cause still needs direct confirmation.
