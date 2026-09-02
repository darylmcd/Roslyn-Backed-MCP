# formatter-baseline-generator-concurrent-load-timeout — Diagnose formatter contention

**row:** `formatter-baseline-generator-concurrent-load-timeout` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs:208`
- `eng/generate-format-baseline.ps1`

## Acceptance

- [ ] Distinguish a generator hang from host-wide MSBuild/dotnet-format contention with bounded operator-visible diagnostics.
- [ ] Prove the generator contract completes or fails with an actionable classification under concurrent repository build/test load.
- [ ] Preserve the five-minute safety bound unless repeated evidence justifies a measured replacement; do not hide contention by only increasing the timeout.

## Evidence

- A 2026-09-01 required `just ci` retry timed out the formatter baseline generator at five minutes while another repository testhost was active; the exact test passed in 49 seconds immediately after competing testhosts exited.

## Context

Treat this as test-infrastructure reliability work. Keep generated baseline semantics and deterministic inventory assertions unchanged.

## Amendment — 2026-09-02 (cold plan-deepener; verified against live source, no code shipped)

Row is **shovel-ready**. Anchor note: `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs:208` resolves but is one hop off the construct — it is the `RunGeneratorCheckAsync()` call site; the timeout itself is `:19` plus `:277-287`. Work from `:249-290`.

**Root cause — the five-minute bound is not the defect, the blindness around it is.** `FormatterBaselineContractTests.cs:277-287` starts a 5-minute CTS (`_processTimeout` at `:19`), kills the tree, and throws `TimeoutException` carrying only the bound; the `stdoutTask`/`stderrTask` started at `:275-276` are **abandoned**, so the killed generator's partial output — the only evidence of where it stalled — is discarded at exactly the moment it is needed. `eng/generate-format-baseline.ps1:83-93` runs `dotnet restore` then `dotnet format` back-to-back with no phase marker or elapsed time, so even captured output could not separate restore contention from format contention. That matches the 2026-09-01 evidence exactly.

**Hard constraint discovered while reading:** generator **stdout is the byte-compared determinism payload** (`FormatterBaselineContractTests.cs:211-214`, generator `:198`). Every new diagnostic MUST go to stderr or the determinism assertion breaks. Also: use raw `[Console]::Error.WriteLine`, NOT `Write-Error` — `$ErrorActionPreference='Stop'` at `generate-format-baseline.ps1:49` would abort the script.

**Approach — add signal, not slack; `_processTimeout` stays at 5 minutes verbatim.**
1. `eng/format-diagnostic-contract.ps1` — add one shared `$formatPhaseMarkerPrefix` alongside the existing `$formatDiagnosticPattern`/`$formatTruncationMarker`, so emitter and parser share one grammar (the convention `FormatterBaselineContractTests.cs:181-202` already enforces).
2. `eng/generate-format-baseline.ps1` — emit `restore start|end` and `format start|end` markers with elapsed ms on stderr around `:83-89` and `:91-93`. Markers must not enter `$formatOutput` (`:92` merges only the `dotnet format` child's stderr), leaving the truncation check at `:103-106` and the JSON payload at `:190-208` untouched.
3. `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs` — in `RunGeneratorCheckAsync` (`:249-290`) snapshot competing processes before `Process.Start` and again on timeout (`dotnet`/`testhost`/`MSBuild`/`VBCSCompiler`, excluding this pid and the generator tree, per-process `Win32Exception` swallowed, capped at 10); after `Kill(entireProcessTree: true)` **drain** the already-started read tasks under a bounded wait instead of abandoning them. Extract a testable pure static `ClassifyGeneratorTimeout(...)` returning `generator-never-started` / `generator-hang` / `host-contention`, and put that verdict plus per-phase elapsed and the competing-process list in the `TimeoutException` message. Emit the same phase-timing line on the SUCCESS path too, so TRX runs accumulate the repeated evidence acceptance bullet 3 requires before any measured bound replacement.

**Scope:** production 2 — `eng/generate-format-baseline.ps1`, `eng/format-diagnostic-contract.ps1`. Tests 1 extended — `FormatterBaselineContractTests.cs`. Fanout probe: 4 consumers of the shared contract, only the generator must change (`eng/verify-changed-format.ps1` dot-sources and ignores the additive variable; `Skills/ChangedFormatGateScriptTests.cs` asserts on the gate's own streams).

**Bad code found in-scope (Directive #3, fixed by this row):** `FormatterBaselineContractTests.cs:275-287` abandons `stdoutTask`/`stderrTask` on the timeout throw path, discarding the diagnostics.

**Deliberately deferred — do NOT do it in this PR:** the test pays two full `dotnet restore` passes because `RunGeneratorCheckAsync` omits `-NoRestore` (`:264-271` vs generator `:83-89`). Real cost driver, but dropping restore weakens the fail-closed truncation contract at `:103-106` — it is exactly the "measured replacement" the new success-path timings are meant to justify, not a blind change. Tracked by row `formatter-baseline-generator-double-restore-cost`.
