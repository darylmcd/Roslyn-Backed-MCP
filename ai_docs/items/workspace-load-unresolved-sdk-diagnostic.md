# workspace-load-unresolved-sdk-diagnostic — Surface an unavailable workspace SDK

**row:** `workspace-load-unresolved-sdk-diagnostic` · **pri:** `Medium` · **size:** `M` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:104-113` — solution and project load.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1520` — stale reload boundary.
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:524` — gated auto-reload.
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:151-170` — public error classification.
- `docs/product-contract.md:89-93` — stable error fields.

## Acceptance

- [ ] When the workspace pins an SDK absent from the server process, load and stale reload expose the requested SDK and a corrective diagnostic. Preserve the underlying exception in internal diagnostic evidence.

## Evidence

- SnipCue SHA bafcaeb after SDK 11.0.100 pin: heartbeat reported Roslyn 5.1.1 live. Loading the concrete solution and unit project with autoRestore=false returned "InvalidOperationException: The operation is not valid in the current state. Check the tool contract and retry." A previously cached workspace reloaded twice with opaque InternalError. Correlation IDs df2fd723448a4c849169d75a892b8e1f and 7de9d1e91a124b18a43c304e4fbf037b.
- From the same checkout the default dotnet reports "A compatible .NET SDK was not found" and "Requested SDK version: 11.0.100". The scoped SDK installation successfully built and tested the same project. Missing SDK is the suspected trigger. The server exception cause still needs direct confirmation.

## Notes

5.x release screen: qualify the underlying exception on released 5.1.1 and current source for solution and project load, explicit reload, and gated stale auto-reload in both raw protocol eras. Preserve existing category, exceptionType and correlationId semantics. Only expose validated SDK identity and guidance through a compatible field. If the complete fix requires changing a stable error contract, resume at the next operator-approved major with an ADR, migration note and raw-wire coverage.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
