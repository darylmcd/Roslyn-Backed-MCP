# format-gate-pipe-filename-diagnostics — Report formatter findings for Linux filenames containing a pipe

**row:** `format-gate-pipe-filename-diagnostics` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/verify-changed-format.ps1` (`$bucket.Split('|')` in the findings report)
- `tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs`

## Acceptance

- [ ] A Linux process-level regression fails on the current script for a changed C# filename containing `|`, then passes with the full path and diagnostic id reported accurately.
- [ ] Preserve finding counts, baseline multiplicity, and Windows behavior.

## Evidence

- Cold review during the 2026-11-05 remediation run found that `$bucket` is built from `"$relativePath|$diagnosticId"`, then split at every pipe; a valid Linux filename containing `|` truncates the displayed path and shifts the diagnostic id.

## Context

- The case-sensitive path identity fix uses an OS-aware comparer. Delimiter parsing is a separate pre-existing reporting mechanism.
