# executed-log-line-leaks-filter-and-paths — Stop the gated Executed log line from logging caller filter text and absolute paths

**row:** `executed-log-line-leaks-filter-and-paths` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`

## Acceptance

- [ ] The pre-existing `LogCommandExecuted` record no longer carries the full target path or the joined arguments (caller `--filter` text, admission-augmented `-p:` values and roots); the new allowlist boundary records (`CommandProcessCompleted`, `CommandGateCompleted`) are the only per-command records, or the Executed record is reduced to the same allowlist.
- [ ] Red-first test feeds a sentinel filter and absolute path through the real emission path and asserts neither reaches any log line.
- [ ] Release impact checked first: removing observable detail from an operator log is acceptable only if the JSON-lines fields stay documented; if a client could depend on the old message, record an ADR and ship in the next major.

## Evidence

- PR 2060 cold review (2026-11-10): `GatedCommandExecutor.cs:~129` still logs the target path and joined arguments including caller `--filter` (`TestRunnerService.cs:~414`); the new boundary records duplicate its duration and exit code.

## Context

- Pre-existing, not introduced by PR 2060. Decide retire versus sanitise after the release-impact check.

## Notes

5.x screen at a12412bb: GatedCommandExecutor.cs emits published event 3001 with TargetPath, Arguments and ExitCode. The JSON-lines projection retains named fields and the rendered message. Removing or repurposing those values may break existing consumers. If the change is breaking, reopen at the next major with ADR and migration from LogCommandExecuted to the safe boundary events. Check the separate timeout path that embeds the same argument list in an exception and establish what its consumers actually observe.
