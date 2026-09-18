# boundary-rejection-redacted-to-generic-schema-error — boundary-rejection-redacted-to-generic-schema-error

**row:** `boundary-rejection-redacted-to-generic-schema-error` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs` — throw the public-message variant so the boundary reason survives redaction.
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs` — assert the client-visible message names the boundary.

## Acceptance

- [ ] A `workspace_load` for an absolute, existing path outside the sanctioned-root boundary returns a client-visible message that names the boundary as the cause, not `Parameter 'path' is invalid`.
- [ ] The response no longer emits a `schemaHint` implying the path was malformed or relative when the path was well-formed and the boundary was the rejection reason.
- [ ] The rejection stays free of information the boundary is meant to withhold — it names the cause, not the configured root paths, unless an existing disclosure rule already permits it.
- [ ] A regression test pins the client-visible message for the outside-boundary case.

## Evidence

- Reproduced 2026-09-18 against the live 4.2.0 plugin: `workspace_load` with `C:/Code-Repo/DotNet-Network-Documentation/NetworkDocumentation.sln` (verified present on disk, absolute, both slash styles) returned `{"category":"InvalidArgument","exceptionType":"ArgumentException","message":"Parameter 'path' is invalid. Check that all required parameters are provided and values match the expected types.","schemaHint":"workspace_load(path: string — Absolute path to a .sln, .slnx, or .csproj file)"}`.
- Positive control in the same session: `C:/Users/daryl/.claude/plugins/marketplaces/roslyn-mcp-marketplace/SampleSolution.slnx` loaded cleanly (3 projects, 24 documents, 0 diagnostics), proving the loader works and the path — not its format — was refused.
- `ClientRootPathValidator.cs:106-108` throws `new ArgumentException($"Path '{path}' is outside the configured sanctioned-root boundary.", nameof(path))` — an accurate message.
- `ToolErrorHandler.cs:12-17` documents that arbitrary `ArgumentException.Message` values are redacted and only `PublicArgumentException.PublicMessage` reaches the client, so the accurate message above is replaced by the generic parameter-schema template.

## Context

The redaction is deliberate and correct as a default; the defect is that this call site never opted into the public-message channel that the handler already provides, so a boundary refusal is indistinguishable from a malformed-argument error. Cost is misdiagnosis: the caller sees a schema hint about absolute paths and hunts a path-format bug that does not exist.

Published repo (`roslyn-mcp@roslyn-mcp-marketplace`), so this is a consumer-facing error contract — treat a message change as contract-visible.
