# preview-diff-whitespace-omission — Preserve whitespace mutations in preview diffs

**row:** `preview-diff-whitespace-omission` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs:36` whitespace-ignoring diff builder; lines 108-110 drop all-Unchanged output.
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs:53` empty-hunk omission; line 86 sends different strings to shared diff generator.
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:509` formatted solution diff; line 511 stores that solution.
- `src/RoslynMcp.Roslyn/Services/EditService.cs:372` explicit multi-file edit exposes empty unifiedDiff entries.
- `tests/RoslynMcp.Tests/DiffGeneratorTests.cs:9` misleading test name; shared helper regression location.

## Acceptance

- [ ] Preserve all source whitespace mutations represented by stored changes, including leading/trailing spaces, whitespace-only lines, and mixed content/whitespace edits. Investigate newline/final-newline comparison separately within the same text-fidelity mechanism; do not claim exact text parity from non-whitespace content alone.
- [ ] Observe old-behavior failing regressions, then passing fixed results. Retain identical-input empty output, bounded hunks and explicit truncation reporting.
- [ ] Re-derive shared producers/consumers; prove changed documents have visible hunks in formatting and explicit text-edit previews, and the preview/apply text matches. Unchanged formatter output must remain empty.
- [ ] Rename Identical_Texts_Returns_Header_Only to match its empty-string assertion; repair stale same-file explanations encountered while establishing the corrected diff contract.

## Evidence

- Re-vet base: `c43e8fa6995151b22be6feb2c4a1020263b88ce5`. Original filing: PR #1747, immutable head `644d2080e90e0e4c4046690b44552ae27be5e1f7`.
- `DiffGenerator.cs:36`: `var diff = diffBuilder.BuildDiffModel(oldText, newText);`; lines 108-110: `if (hunksWritten == 0 && !truncated) { return string.Empty; }`.
- Installed DiffPlex 1.9.0 nuspec identifies upstream commit `3cb6415c331a0af2f2686154cb77169ef29b63ce`. Read that immutable source through GitHub: [InlineDiffBuilder.cs](https://github.com/mmanela/diffplex/blob/3cb6415c331a0af2f2686154cb77169ef29b63ce/DiffPlex/DiffBuilder/InlineDiffBuilder.cs#L24) declares `public DiffPaneModel BuildDiffModel(string oldText, string newText) => BuildDiffModel(oldText, newText, ignoreWhitespace: true);`.
- `SolutionDiffHelper.cs:53`: `if (string.IsNullOrEmpty(change.UnifiedDiff)) { return false; }`; line 86 sends old/new text to the shared generator even after unequal-text check. Whitespace-only differences are silently omitted.
- `EditService.cs:372-373`: `var unified = DiffGenerator.GenerateUnifiedDiff(sourceText.ToString(), merged.ToString(), filePath);` then `changes.Add(new FileChangeDto(filePath, unified));`. Explicit edits instead retain changed entries with empty hunks.
- Inspected retained historical artifact `C:/Users/<user>/AppData/Local/Temp/snipcue-remediate-20261004/20261004T122130Z_backlog-remediate/whitespace-preview-0522-evidence.json`: multi-file preview reports 13 changes with empty unifiedDiff; format response reports changes=[] and records before/after artifact paths/hashes. No fresh preview/apply or DLL execution occurred during re-vet; source establishes the omission independently.
- `DiffGeneratorTests.cs:9` names `Identical_Texts_Returns_Header_Only`; line 17 asserts `Assert.AreEqual(string.Empty, result);`.
- Live main contains no equivalent shared whitespace-diff row. refactor-preview-trivia-nits concerns syntax/trivia producers, not shared comparison omission.

2026-10-23 live formatting preview twice returned changes=[] for NativeDocumentDegradedAuthorityTests.cs although the current loaded source contains an unindented fixture assignment directly inside try and .editorconfig requires four-space indentation. This is current tool evidence, not an implementation gate result; retain this separate tracked preview defect while formatting the active cleanup change.

2026-10-23 live follow-through: format_document_apply on the empty-change preview reported the source file applied. The parent compared the before/after file text to qualify whether the actual formatting mutation was omitted from preview; this is a tooling proof, not a repository validation gate.

2026-10-23 current TestRunnerService.NativeProtocols.cs format_document_preview token cddb427b67b4496bbe868cd32a1c7944 again reports changes[] despite current cold-read visibly unindented owning trybody98-216 and protocol body237 onward. Exact original saved native-protocols-before-format25.cs; applying formatter then byte/leading-whitespace comparison next. No false empty diff approval claim.

2026-10-23 applied cddb427b67b4496bbe868cd32a1c7944 after saving exact NativeProtocols original: success appliedFiles actualsource; before/after bytes differ, all356 lines identical after trimStart (leading whitespace only). Confirms current preview omits real formatting diff; no source semantic changes from this formatting step.
