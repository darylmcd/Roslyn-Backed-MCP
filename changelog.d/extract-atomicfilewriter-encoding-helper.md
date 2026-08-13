---
category: Maintenance
---

- **Maintenance:** Extracted the BOM/encoding-sniffing logic shared by `AtomicFileWriter`, `EditorConfigService`, `ProjectMutationService`, `EditService`, `UndoService`, and `CsprojSemanticEquality` into a single `SourceFileEncoding` helper (`src/RoslynMcp.Roslyn/Helpers/SourceFileEncoding.cs`). Removes an inverted dependency where non-atomic writers borrowed `AtomicFileWriter` purely as an encoding sniffer, and removes a full-file decode + discarded snapshot allocation from every mutation-apply write path that only needed the detected encoding. No behavior change.
