# workspace-id-named-args-validation — Name workspace-sensitive validation arguments

**row:** `workspace-id-named-args-validation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`
- `tests/RoslynMcp.Tests/ValidationToolsIntegrationTests.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the two files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and both targeted test classes to pass.

## Evidence

The live census found positional validation and failure-envelope calls whose required arguments follow `workspaceId`.

## Context

Split from `workspace-id-optional-named-argument-prerequisite`.
