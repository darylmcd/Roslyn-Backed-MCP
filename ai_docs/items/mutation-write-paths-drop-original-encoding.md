# mutation-write-paths-drop-original-encoding — apply paths silently re-encode files as UTF-8-no-BOM

**row:** `mutation-write-paths-drop-original-encoding` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:133` (`AtomicFileWriter.WriteAllTextAsync` → `File.WriteAllTextAsync(tmp, content, ct)`, no `Encoding` argument)
- `src/RoslynMcp.Roslyn/Services/EditService.cs:715`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:565`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:375` (`File.WriteAllLines`, same default)

## Acceptance

- [ ] The apply path preserves the file's detected pre-mutation encoding/BOM (thread the `SourceText`/detected `Encoding` through `AtomicFileWriter` instead of relying on `File.WriteAllTextAsync` / `File.WriteAllLines` defaults).
- [ ] A test writes a UTF-8-BOM and a UTF-16 fixture, runs `apply_text_edit` / `apply_project_mutation` / `set_editorconfig_option`, and asserts the post-apply BOM and encoding are unchanged.

## Evidence

- Code-quality review of PR #1144 (`direct-mutation-undo-byte-fidelity`), traced the full write chain (not hypothesized): `EditService.cs:715` and `ProjectMutationService.cs:565` call `AtomicFileWriter.WriteAllTextAsync`, whose body (`CompositeApplyOrchestrator.cs:133-137`) forwards to `File.WriteAllTextAsync(tmp, content, ct)` with no `Encoding` argument — .NET's default there is UTF-8 without BOM; `EditorConfigService.cs:375` calls `File.WriteAllLines` with the same default. No `Encoding` is threaded anywhere in the chain, so a UTF-8-BOM or UTF-16 file is rewritten as UTF-8-no-BOM on every apply.

## Context

Spin-off from the `direct-mutation-undo-byte-fidelity` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1144). That initiative fixed only the undo/restore half of byte fidelity; the forward apply path is still lossy for non-UTF-8-no-BOM source files.
