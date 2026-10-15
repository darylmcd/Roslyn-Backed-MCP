# sourcefileencoding-editorconfig-transaction-comment — Correct stale editorconfig write-path commentary

**row:** `sourcefileencoding-editorconfig-transaction-comment` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SourceFileEncoding.cs:13`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397`

## Acceptance

- [ ] Replace the stale `File.WriteAllLines` claim with the current byte-rendering and owned file-transaction write path; retain accurate extraction history and encoding semantics.
- [ ] Confirm the documented call chain resolves in current source without changing runtime behavior or mixing this documentation repair with the separate Core relocation row.

## Evidence

| Source | Current construct |
|---|---|
| `SourceFileEncoding.cs:13` | `EditorConfigService` is described as writing through `File.WriteAllLines(string, IEnumerable{string}, Encoding)`. |
| `EditorConfigService.cs:397-406` | `StreamWriter` renders a memory buffer using `SourceFileEncoding.FromBytes(existingBytes)`; `transaction.WriteBytes(writtenBytes)` publishes the bytes. |

Source-only tracing during approved editorconfig global design re-vet. Independent pre-existing documentation debt; no runtime failure or implementation delivery claimed.
