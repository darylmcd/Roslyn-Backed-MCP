# preview-refusal-public-reasons — Emit reviewed reasons for move_type_to_file refusals

**row:** `preview-refusal-public-reasons` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] All six refusal throws in `TypeMoveService` — document-not-found (:28), source-not-a-C#-compilation-unit (:31), unsupported type kind (:65), type-not-found (:70), single-top-level-type (:78), target-file-already-exists (:94) — throw `PublicInvalidOperationException` (already `public` in `RoslynMcp.Core.Services`, already referenced by this file) instead of a plain `InvalidOperationException`.
- [ ] Each message is server-authored: it names the refusal cause and the corrective action, and echoes no absolute or solution-relative file path. A caller-supplied type name MAY be retained; the three throws that currently interpolate `sourceFilePath` / `resolvedTargetPath` are rewritten to drop the path.
- [ ] `tests/RoslynMcp.Tests/TypeMoveTests.cs` is updated: the existing `Assert.ThrowsExactlyAsync<InvalidOperationException>` assertions (:123, :141, :161, :182, :207) become `PublicInvalidOperationException` — `ThrowsExactly` is exact-type, so they fail otherwise — and at least two refusals pin their exact message text.
- [ ] `ToolErrorHandler.cs` is NOT edited. The generic `InvalidOperationException` fallback ("The operation is not valid in the current state. Check the tool contract and retry.") remains the behavior for every throw site not converted here; the boundary half of the contract is already locked by `TestRunFailureEnvelopeTests` (:958, :993) and needs no new coverage.
- [ ] No absolute path, solution path, or secret appears in any newly public message.

## Evidence

- Preview refusals across six tools in seven codex sessions returned generic `InvalidOperation`/`InvalidArgument` text carrying no reason, and every refusal ended with the agent abandoning the tool for hand edits; `move_type_to_file_preview` alone produced six of the twelve refusal envelopes, and the server already knew the cause.
- `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`, finding `preview-refusal-public-reasons` (§4.5), proposed site 1.
- Scoped to site 1 only. Site 2 (EditService's overlapping-edit `ArgumentException`, `EditService.cs:578-584`) is blocked: `PublicArgumentException` is `internal` to RoslynMcp.Host.Stdio (`ToolErrorHandler.cs:15`) and unreachable from RoslynMcp.Roslyn, so that slice must first promote the type to `RoslynMcp.Core.Services` — a sibling row. Site 3 (StructuredCallToolFilter naming the unknown argument key) has a stale anchor: the file is 48 lines at HEAD after the pipeline decomposition and no longer inspects raw arguments; it needs a fresh investigation of the intercept point, not a line-anchored edit.
