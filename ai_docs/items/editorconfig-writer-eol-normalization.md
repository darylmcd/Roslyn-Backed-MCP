# editorconfig-writer-eol-normalization — Preserve the file's line endings when set_editorconfig_option rewrites an .editorconfig

**row:** `editorconfig-writer-eol-normalization` · **pri:** `Low` · **size:** `S` · **verified:** `df2f06c1dc495c3e0c4dfaf4ae7e67a54b044efa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs`

## Acceptance

- [ ] `SetOptionAsync` (~:403-406) no longer rewrites every line with the platform `WriteLine`; the file's existing line endings (and final newline) are preserved byte for byte outside the edited assignment.
- [ ] Red-first test writes a CRLF file on a LF host (and the reverse) and asserts only the edited line changes.
- [ ] RELEASE IMPACT FIRST: this changes the bytes a stable tool writes; per `docs/release-policy.md:24-26` and ADR 0028 precedent confirm it is a correction a client cannot depend on, else bundle it with the v6.0.0 editorconfig writer rows.

## Evidence

- EditorConfig reader/writer planning review (2026-11-10): the writer normalizes EOL for the whole file. Related rows `editorconfig-section-source-path-matching` and `editorconfig-writer-effective-section-precedence` are held for v6.0.0.

## Context

- Reader fidelity row `editorconfig-reader-fidelity` shares the line-model work (`SourceText.Lines`) but is separate.
