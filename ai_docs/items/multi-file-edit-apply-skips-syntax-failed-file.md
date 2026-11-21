# multi-file-edit-apply-skips-syntax-failed-file — apply_multi_file_edit: refuse (not silently skip) a file that fails the syntax check

**row:** `multi-file-edit-apply-skips-syntax-failed-file` · **pri:** `Medium` · **size:** `S` · **verified:** `79ad03bf9291670cb708a6501b1b6297a29f89cf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] A batch with one syntax-failing file reports the failure (Success:false or a per-file failure the response already carries) instead of Success:true with the file unedited (EditService.cs:~259-265); red-first test; apply_multi_file_edit is experimental, so no stable-surface rule applies.

## Evidence

- Plan of edit-syntax-check-project-parse-options (section 9 sketch).
