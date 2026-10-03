# ADR 0010: Validation verdicts require completed compilation

Status: Accepted, 2026-09-15.

## Context

The experimental `validate_workspace` and `validate_recent_git_changes` tools could
report `clean` when compilation returned no errors after cancellation or before
all selected projects completed. Their diagnostic merger already recognized this
incomplete state, but their verdict calculation did not. A later diagnostic pass
without errors cannot prove that the earlier compilation finished.

The related-test phase also caught the internal validation timeout as a generic
runner exception, changing a retryable timeout into a non-retryable test failure.

## Decision

Use the same compilation-completeness predicate for diagnostic merging and the
aggregate verdict: compilation is incomplete when `Cancelled` is true or when
both project counts are present and `CompletedProjects < TotalProjects`.

Preserve the existing precedence of compiler errors, retained diagnostic errors,
test failures, and zero-test runs. If none applies, incomplete compilation returns
`compile-incomplete` instead of `clean`. A completed zero-project selection and
uncancelled legacy results without project counts retain their existing behavior.
Do not infer a compiler failure from `Success=false` alone.

Run related tests under the runner's `TestTimeout`, outside the 25-second internal
validation phase cap. Both tools preserve the runner's `Timeout` failure envelope
with `IsRetryable=false` and add a warning identifying `test_run`. The aggregate
verdict is `timeout` after compiler and retained diagnostic errors, before test
failures and zero-test runs. Compile, diagnostics, and discovery retain their
internal phase cap and retryable timeout boundary. Caller cancellation still
propagates; unexpected runner exceptions still produce `test-failure`.

Capture compilation, diagnostics, discovery, and workspace generation under the
source read gate. Release that gate and its `RequestTimeout` before testing; only
the caller's cancellation token and the runner's `TestTimeout` apply to test
execution. Preserve the captured scope and compilation verdict. If the workspace
changes or closes during testing, add a `workspaceChangedDuringRun` warning and
replace a would-be `clean` verdict with `workspace-changed`. A snapshot already
stale before testing also produces `workspace-changed` with a separate
`workspaceSnapshotWasStale` warning: refresh the workspace before re-running.
Known compiler,
diagnostic, test, timeout, and incomplete-compilation findings retain precedence.

`workspace_fork_apply` holds the source writer lock only while copying the source
and replaying the preview. Fork restore, loading, validation, and tests run after
that lock is released, so source writes can proceed during fork tests.
Capture the loaded fork workspace id before validation/testing so cancellation
closes the session before its files are deleted.

## Compatibility and migration

These are observable status corrections on experimental tools. They may ship in
the next minor release under the experimental compatibility policy; no stable
tool request, response shape, or support tier changes. The experimental
`workspace_fork_apply` tool also embeds this validation result.

Consumers must treat `compile-incomplete` as non-passing and retry compilation or
inspect `compileResult.cancelled`, `completedProjects`, and `totalProjects` before
proceeding. A zero error count alone does not prove success. Consumers previously
treating a test-phase deadline as a test assertion failure should instead handle
`timeout` using the runner's non-retryable failure envelope: narrow the test filter
or raise `TestTimeout` before retrying. Internal in-memory phase timeouts remain
retryable. JSON, summary, and Markdown rendering retain the aggregate verdict.
Consumers must treat `workspace-changed` as non-passing and re-run validation
against the new source snapshot.

## Validation

Service-entry regressions use real isolated workspace and Git scope with controlled
compile, diagnostic, and test services. They cover cancelled, partial, zero-completed,
and complete compilation, retained diagnostic/test failures, deterministic
test-phase cancellation, retryability, and outer cancellation through both tools'
service entry points. Existing diagnostic-merger regressions remain in place.
