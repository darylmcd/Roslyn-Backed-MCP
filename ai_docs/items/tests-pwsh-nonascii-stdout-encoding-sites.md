# tests-pwsh-nonascii-stdout-encoding-sites — Make non-ASCII pwsh stdout deterministic at the remaining spawn sites

**row:** `tests-pwsh-nonascii-stdout-encoding-sites` · **pri:** `Medium` · **size:** `M` · **verified:** `cd90857cbce0179e4e8a6202ca4c321189736ea8`

## Anchors

- `eng/verify-version-drift.ps1:120`
- `eng/verify-breaking-version-bump.ps1:181`
- `tests/RoslynMcp.Tests/Skills/ArchiveOldReportsScriptTests.cs:219`
- `tests/RoslynMcp.Tests/Skills/ExperimentalAgeAuditScriptTests.cs:192`
- `tests/RoslynMcp.Tests/VersionDriftGateTests.cs:64`

## Acceptance

- [ ] For every eng/*.ps1 script whose stdout or stderr is read by a test or tool and can contain non-ASCII text (the survey found about 21 eng scripts with non-ASCII, mostly messages; `eng/verify-version-drift.ps1:120` and `eng/verify-breaking-version-bump.ps1:181` print an em dash and section sign), set `[Console]::OutputEncoding` to UTF-8 without BOM on the script side and decode as UTF-8 on the test side, or migrate the raw `ProcessStartInfo` pwsh spawn to `PwshScriptRunner`.
- [ ] Raw pwsh spawns still reading stdout without an explicit encoding are listed with `file:line` and each is fixed or shown ASCII-only: `ArchiveOldReportsScriptTests.cs:219`, `ExperimentalAgeAuditScriptTests.cs:192` and `:212`, `VersionDriftGateTests.cs:64`, `HookConfigurationTests.cs:364` and `:392`, `FormatterBaselineContractTests.cs:550`, `:716` and `:1195` (grep `RedirectStandardOutput` under `tests/` for others, e.g. `LocalToolReinstallProcessOwnershipTests.cs:132`, `ToolUpdateOwnedProcessShutdownTests.cs:243`).
- [ ] A test per fixed script proves a non-ASCII character round-trips (red on the old code).
- [ ] `AggregatePromotionScorecardsScriptTests.RunPwshScript` is migrated to `PwshScriptRunner` (its bare `WaitForExit(60_000)` with synchronous reads can deadlock on a full pipe, unlike the shared runner).

## Evidence

- Remainder of the fix that unblocked main (branch `rem/fix-scorecard-encoding`): that PR covers `eng/aggregate-promotion-scorecards.ps1`, `eng/propose-promotion-scorecard-backlog-rows.ps1`, `RunPwshScript` and `PwshScriptRunner`. The reviewer found the remaining raw sites unproven and mostly ASCII-only, so they were split out per the merge-before-widening rule.

## Context

- Mechanism: pwsh redirected stdout is encoded with `[Console]::OutputEncoding` (OEM cp437 on the Windows runner), so U+2192 becomes byte 0x1A; a test-side UTF-8 decode only helps when the script also emits UTF-8.
- Flag-only siblings (separate mechanism, product side): `src/.../DotnetCommandRunner.cs:224`, `ScriptWorkerProcess.cs:138`, `eng/profile-large-solution.ps1:81`, `eng/generate-format-baseline.ps1:143`.
