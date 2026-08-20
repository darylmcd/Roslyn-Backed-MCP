## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:137` — timeout shell sets `Arguments` / `TargetPath`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:332` — serializes `result.Execution` verbatim to the client
- `src/RoslynMcp.Core/Models/CommandExecutionDto.cs:11`

## Acceptance

- `test_run` (and sibling validation tools) no longer publish the absolute `--results-directory` temp path, the absolute target project path, or the raw caller-supplied `--filter` value through `Execution.Arguments` / `TargetPath` / `WorkingDirectory`; a redacted or workspace-relative command shape is returned instead.
- Regression test asserts a serialized `test_run` response carrying a sentinel-bearing `--filter` and an absolute target path exposes neither — on BOTH the timeout-envelope path and the ordinary parse path.

## Evidence

Traced end to end during the Step 8b review of PR #1299: `TestRunnerService.cs:85-100` builds argv with `targetPath`, the absolute temp results directory, and the caller `--filter`; `:137-146` puts that list into the timeout shell's `Arguments`/`TargetPath`; `DotnetOutputParser.cs:145-152` passes the execution into `TestRunResultDto`; `ValidationTools.cs:332-334` serializes `result.Execution` to the client.

**This is the same disclosure row `test-runner-timeout-error-detail-redaction` targeted, still reaching the client by a second route.** PR #1299 redacted `FailureEnvelope.Summary` and `StdErrTail`; it did not touch `Execution`. Its changelog fragment originally claimed the paths and `--filter` were no longer published — that claim was corrected before merge precisely because this route remains.

Pre-existing and true on the success path too, so not introduced by PR #1299.

Source: code-quality review of PR #1299, sweep 20260819T180531Z.
