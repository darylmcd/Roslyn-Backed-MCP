# workspace-load-typed-response-composition — Compose workspace-load responses as typed DTOs

**row:** `workspace-load-typed-response-composition` · **pri:** `Low` · **size:** `S` · **deps:** `workspace-close-postcommit-drain-contract`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — `SerializeWorkspaceLoadResult`.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceCachePrewarmTests.cs`

## Acceptance

- [ ] Represent status plus optional prewarm data with one typed response composition.
- [ ] Serialize once through the shared JSON options; do not serialize, parse into `JsonNode`, mutate, and serialize again.
- [ ] Preserve current property names, omission rules, indentation, and prewarm success/failure variants.
- [ ] One prewarm-outcome matrix deep-compares the typed response with the existing public shape.

## Evidence

- `SerializeWorkspaceLoadResult` round-trips a typed status through JSON text and `JsonNode` solely to append the prewarm property.
