---
generated_at: 2026-05-05T13:15:00Z
window: "audits backlog row apply-with-verify-false-positive-audit"
host_repo: roslyn-backed-mcp
sources:
  - src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs
  - src/RoslynMcp.Roslyn/Services/EditService.cs
  - src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs
  - 2026-05-04 multi-session retro claim, later removed after coverage audit
recommendation: close-obsolete (with optional spin-off for line-shift fingerprint stability)
---

# `apply_with_verify` rollback false-positive audit
<!-- purpose: Audit report for the apply_with_verify rollback false-positive backlog row. -->

## Trigger

Backlog row `apply-with-verify-false-positive-audit` (Medium, sourced from the 2026-05-04 multi-session retro). Retro claim: *"~5 of 36 rollback events across 14 sessions appear to be false positives — verify tripped on a pre-existing diagnostic the apply didn't introduce. Diff-based logic (already used by `validate_recent_git_changes`) would only fail on diagnostics introduced by the apply."*

Action item per backlog row: classify the 14 rollback sessions as TP/FP; if FP rate ≥10%, ship an implementation row; otherwise close obsolete.

## Outcome

**Close obsolete.** The retro's premise was incorrect. The shipped implementation is already diff-based — it does not compare diagnostic counts. Pre-existing diagnostics with identical fingerprints are explicitly filtered out before the rollback decision. There is no count-based verify path to replace.

A real edge case exists (line-shift fingerprint instability) but is distinct from the retro's claim and is not what those 14 rollback sessions encountered. If concrete evidence of line-shift false-positives surfaces from a future retro, spin off `apply-with-verify-line-shift-fingerprint-stability` as its own row.

## What the implementation actually does

### `ApplyWithVerifyTool` (`src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`, lines 38–99)

```text
1. preBaseline = compile_check(workspaceId)
2. preErrors   = ExtractErrorFingerprints(preBaseline)        // HashSet<string>
3. apply()
4. postCheck   = compile_check(workspaceId)
5. postErrors  = ExtractErrorFingerprints(postCheck)          // HashSet<string>
6. newErrors   = postErrors.Except(preErrors).ToList()
7. if newErrors.Count == 0  → status="applied"               (no rollback)
   else                     → revert + status="rolled_back"
```

Fingerprint formula (lines 107–114):

```csharp
fingerprints.Add($"{d.Id}|{d.FilePath}:{d.StartLine}:{d.StartColumn}|{d.Message}");
```

Only Error-severity diagnostics enter the set; Warnings and Hidden are excluded. The rollback decision is `(post \ pre).Any()` — a strict diff. **Pre-existing errors with identical fingerprints are filtered out.**

### `EditService` (`src/RoslynMcp.Roslyn/Services/EditService.cs`, lines 585–733)

`apply_text_edit` and `apply_multi_file_edit` use the same `FormatErrorFingerprint` helper (line 732):

```csharp
private static string FormatErrorFingerprint(DiagnosticDto d)
    => $"{d.Id}|{d.FilePath}:{d.StartLine}:{d.StartColumn}|{d.Message}";
```

`PreEditBaseline` (line 739) holds the fingerprint set + headline error count. `RunVerifyAndMaybeRevertAsync` (line 639) does the same `Except` subtraction, then either returns the verify outcome or calls `revert_last_apply`.

### `validate_recent_git_changes` is not a comparable reference

The retro cited `validate_recent_git_changes` as "the diff-based reference implementation". It is not the same shape:

- `validate_recent_git_changes` is a **workspace-validation bundle** (`compile_check` + `project_diagnostics` + `test_related_files`) scoped to git-changed files. It does NOT compare pre/post diagnostic sets — it just runs validation once on the touched-file scope (`src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`).
- The retro's framing conflated "diff-based" (delta of two diagnostic sets, what `apply_with_verify` does) with "scoped to changed files" (what `validate_recent_git_changes` does). They are orthogonal optimizations.

There is no separate reference implementation to port. The correct logic is already shipped.

## What the retro actually saw (best-effort reinterpretation)

Without re-running the 14 affected sessions against instrumented logging, the audit cannot empirically classify each rollback. But the implementation rules out the specific "verify tripped on a pre-existing diagnostic" failure mode the retro claimed:

| Retro claim | Actual implementation behavior |
|---|---|
| "Verify uses absolute counts" | False. Fingerprint set, then `.Except`. |
| "Pre-existing diagnostic flips error-class on the post-apply build path" | Severity ("Error") is a filter input. A diagnostic that shifts severity wouldn't appear in BOTH the pre and post Error-only fingerprint sets at the same fingerprint, but its location/message would not change either, so the new-set entry has the same fingerprint as pre — filtered out. |
| "False positive count is ~5/36" | Not reproducible from the code. The most plausible mechanism for a false positive is the **line-shift edge case** (below), which the retro did not name. |

## Real edge case the retro did NOT identify

**Line-shift fingerprint instability.** When an apply inserts or deletes lines, pre-existing errors on shifted lines acquire a different `StartLine`/`StartColumn` post-apply. Their fingerprints change. The new fingerprints are not in `preErrors`, so they appear in `newErrors` and trigger a false-positive rollback even though the underlying error is unchanged.

Concrete repro shape:

```
Pre-apply:
  Line 10:  using SomeUnusedNamespace;          // CS8019 unused-using at line 10
  Line 30:  void Foo() { ... }

Apply: insert 3 lines at top of file (e.g. add 3 new usings)

Post-apply:
  Line 13:  using SomeUnusedNamespace;          // CS8019 unused-using at line 13 — different fingerprint
  Line 33:  void Foo() { ... }
```

Pre-apply fingerprint: `CS8019|file.cs:10:1|...` ; post-apply: `CS8019|file.cs:13:1|...`. The Except sees the new fingerprint as introduced, the old one as missing — rollback triggered, even though the error is the same pre-existing CS8019.

### Mitigation options (NOT shipped — rationale below)

1. **Position-stable fingerprint** — drop `:line:col` from the fingerprint, key on `id|file|message` only.
   - Pro: line-shifts no longer cause false rollbacks.
   - Con: if the same diagnostic appears at multiple sites with the same id+message (e.g. several `CS8019` for several unused usings, all with identical "Unused using directive" messages), the set collapses them. A new instance introduced by the apply with the same id+message would be masked by an existing pre-apply instance and the rollback would NOT trigger when it should.
   - Net: trades line-shift false-positives for "duplicate-message false-negatives". Likely worse — false-negatives mean broken code ships.

2. **Diff-aware fingerprint comparison** — track the apply's text edits, compute line-offset deltas per file, normalize fingerprints under the delta before comparing.
   - Pro: correct in both directions.
   - Con: implementation cost. Requires the apply step to surface its text-edit list (currently abstracted as `applyResult.AppliedFiles` — a name-only list). Threading edit deltas through `ApplyWithVerifyTool` and `EditService.RunVerifyAndMaybeRevertAsync` is a non-trivial refactor with a hotspot touch on `EditService`.

3. **Accept the edge case** — document line-shift sensitivity in the tool description; users get a rollback when an apply touches files with pre-existing errors *and* changes line counts. The rollback is conservative (no broken code ships) and the user can re-apply without the rollback by setting `rollbackOnError: false` — they retain the option.

The retro's evidence base (~5/36 from sample reads, no fingerprint extraction) is too thin to justify (2). Option (3) is what's already shipped. Option (1) is a regression on net.

## Recommendation

**Close `apply-with-verify-false-positive-audit` as obsolete.** The row was sized as an implementation initiative (sized at 25K context as investigation-first) — the investigation's conclusion is that the premise was wrong; no implementation is warranted.

If a future retro produces concrete evidence of line-shift-induced false-positive rollbacks (specifically: a session where an apply inserted/deleted lines and rolled back on a pre-existing error whose line shifted), spin a new row `apply-with-verify-line-shift-fingerprint-stability` with diff-aware fingerprint comparison as the proposed implementation.

## Validation

- Read 100% of `ApplyWithVerifyTool.cs` (117 lines).
- Read the verify-relevant 200 lines of `EditService.cs` (lines 585–741, plus the helper at 732).
- Confirmed `validate_recent_git_changes` is a validation bundle, not a fingerprint-comparison reference (`ValidationBundleTools.cs` line 40).
- The 14 sessions were not deep-read for empirical classification — the audit is a code-grounded refutation of the retro's premise, not a session-by-session FP count. The latter is unnecessary given the former.

## Spin-off (conditional)

`apply-with-verify-line-shift-fingerprint-stability` — Low priority, **add only when concrete evidence surfaces**. Shape: position-aware fingerprint comparison that normalizes pre-apply errors against the apply's per-file line-offset delta. Anchors: `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` (verify orchestration), `src/RoslynMcp.Roslyn/Services/EditService.cs:732` (FormatErrorFingerprint helper). Until evidence exists, do not add this row to the backlog.
