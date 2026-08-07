# extract-atomicfilewriter-encoding-helper — extract BOM/encoding resolution out of AtomicFileWriter + CsprojSemanticEquality into a neutrally-named helper

**row:** `extract-atomicfilewriter-encoding-helper` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:150` (`Utf8NoBom`)
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:196` (`ResolveWriteEncoding(byte[])`)
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:219` (`ResolveWriteEncoding(Encoding)`)
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:380`
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs:264`

## Acceptance

- [ ] `Utf8NoBom` + both `ResolveWriteEncoding` overloads move to a neutrally-named helper under `src/RoslynMcp.Roslyn/Helpers/` (e.g. `SourceFileEncoding`), with overload names that state their input (`FromBytes` / `FromSourceText`); `AtomicFileWriter` and `EditorConfigService` call the helper instead of each other.
- [ ] The byte→`(Encoding, HasPreamble)` sniff no longer requires decoding the whole file into a `ProjectFileSnapshot`; `CsprojSemanticEquality.CreateSnapshot` consumes the shared sniffer rather than being the sniffer.
- [ ] Existing encoding round-trip tests (`ApplyTextEditVerifyTests`, `EditorConfigServiceTests`, `ProjectMutationIntegrationTests`, `CsprojReserializationTests`, `UndoFileOperationsTests`) pass unchanged.

## Evidence

- Code-quality review of PR #1157 (`mutation-write-paths-drop-original-encoding`): general-purpose encoding utilities were added to `AtomicFileWriter`, an internal helper class physically nested in `CompositeApplyOrchestrator.cs`; `EditorConfigService.cs:380` now depends on "AtomicFileWriter" solely as an encoding sniffer while writing non-atomically. `CsprojSemanticEquality.CreateSnapshot` is now also the BOM detector for `.editorconfig` despite its csproj-scoped name, and fully decodes the file into a string plus allocates a `ProjectFileSnapshot` record just to read one field.

## Context

Spin-off from the `mutation-write-paths-drop-original-encoding` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1157). Cited in the initiative's own plan as a deferred rename to stay within the 4-file cap.
PR #1181 (composite-apply-undo-encoding-still-lossy) adds another caller of the byte-sniff pattern this row wants extracted: CompositeApplyOrchestrator.cs:87 now full-reads every mutated file (ResolveWriteEncoding) then CsprojSemanticEquality.CreateSnapshot re-reads it via StreamReader.ReadToEnd for every non-delete mutation. [source: PR #1181 code-quality review]
