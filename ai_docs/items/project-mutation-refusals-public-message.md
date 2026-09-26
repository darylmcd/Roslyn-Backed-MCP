# project-mutation-refusals-public-message — Return ProjectMutationService refusals verbatim

**row:** `project-mutation-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:674`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`

## Acceptance

- [ ] set_project_property_preview(OutputPath) returns "Property 'OutputPath' is not supported. Allowed properties: …" instead of the generic "Check the tool contract and retry."
- [ ] Duplicate package/project reference, duplicate/last target framework, and CPM-not-enabled refusals throw `PublicInvalidOperationException` with their existing server-authored text.
- [ ] No converted message embeds an absolute path (per `PublicInvalidOperationException` remarks); path-bearing refusals keep the plain exception or switch to a file/project name.
- [ ] Regression test asserts the public message for the OutputPath refusal through the tool error envelope.

## Evidence

- ProjectMutationService.cs:71-737 throws ~20 plain `InvalidOperationException` refusals; `ToolErrorHandler` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:152`) passes only `PublicInvalidOperationException` messages through, so every one reaches the client as "The operation is not valid in the current state." — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). `PublicInvalidOperationException` already exists in `src/RoslynMcp.Core/Services/PublicInvalidOperationException.cs`; this row only converts throw sites.
