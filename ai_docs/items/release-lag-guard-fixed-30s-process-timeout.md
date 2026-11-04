# release-lag-guard-fixed-30s-process-timeout — Attribute and fix verifier process timeout

**row:** `release-lag-guard-fixed-30s-process-timeout` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs` (`ThresholdEnvVariable_OverridesDefaultAndInvalidValuesFallBack`, `RunVerifierAsync`)
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs` (process exit and output drain timing)
- `eng/verify-changelog-fragments.ps1` (verifier and Git operations)

## Acceptance

- [ ] Attribute the hosted 30-second process-exit delay to PowerShell startup, verifier work, Git, or another measured stage using bounded, secret-safe timing evidence from the actual child invocation.
- [ ] Fix the measured root cause without extending the timeout or serializing the test to hide contention. Preserve verifier threshold behavior for all five input values.
- [ ] Reproduce the identified failure at its actual seam on unchanged old code, then pass the same regression after the fix; retain process exit, output drain, cancellation, and termination controls if the shared runner changes.

## Evidence

- PR #1910 run 40927393890 failed `windows-hosted-4-of-4` twice in this test (`changelog verifier timed out after 30 seconds`, elapsedMs 31769 then 32462), while the class passed 5/5 in 9 seconds locally on that head. This was not base-commit proof.
- PR #1951 run 41581270680, job 125736496364 failed `windows-hosted-2-of-4` on the **first** of five verifier calls at `ReleaseLagGuardTests.cs:84`. The process was still running at 30.165 seconds, with the runner in `process exit` phase and zero pipe bytes. Zero bytes do not distinguish startup from script or Git work because successful verification emits output only after those operations. The affected test/helper/script files are unchanged from PR base.
- Unchanged-source PR #1954 (`620159a565287764659440eb66b1068fbf5ed5fc`, zero changed files) passed the full hosted code matrix, including Windows shard 2, in this session. Draft diagnostic PR #1955 at `97c0d47598d661def08f088af590d9b428cc7b64` also passed every hosted check; no timeout occurred to locate the delayed stage. PR #1951 was returned to draft. The failed gate remains unattributed and this row remains open.

## Notes

Evidence 2026-11-04 (remediation review): 30 s timeout on hosted Windows is probably contention, not logic: cold pwsh start plus at least 7 git.exe spawns under Defender scanning, with tests/RoslynMcp.Tests/AssemblyInfo.cs:9 Parallelize(Workers = 0, ClassLevel). PR 1955 (draft, parked) holds an unmerged attempt; root cause not proven.

2026-11-04: PR 1955 (verifier stage trace, 17 test-only calls inside eng/verify-changelog-fragments.ps1) closed unmerged: speculative, edits the shipped script, and never captured a failing trace. Next attempt: on timeout, snapshot the child process tree (names, command lines, CPU) inside PwshScriptRunner's cancellation path, no verifier edits, then wait for one hosted failure. Branch head was 97c0d475.
