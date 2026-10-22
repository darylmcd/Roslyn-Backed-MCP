# Test-process output drain

<!-- scope: in-repo -->

| Concern | Evidence / decision |
|---|---|
| Original failure | Held mutation full gate: VerifyRelease_PropagatesEveryChildScriptFailureAsync reached parent exit, then exceeded its unchanged 30-second output deadline. Clean base full gate passed. |
| Attribution limit | The original full-suite incident was not reproduced. Do not call it environmental, pre-existing, or conclusively attributed to worker starvation. |
| Diagnostic suite | Temporary dedicated-thread sampler observed native exit, reader states, cancellation, pool size and queue depth. No verifier timeout recurred. The run had one formatter failure caused by the temporary instrumentation; it is not qualifying green evidence. Instrumentation was removed. |
| Controlled post-exit reproduction | Two workers occupied by another child's empty redirected pipes; independent child exited with code 37, but its two readers stayed pending with four queued continuations. |
| Controlled output backpressure | One worker; child writes 64 KiB stderr then 64 KiB stdout. Old stdout read occupies the worker, stderr fills, child cannot exit. |
| Permanent negative control | Three new cases ran against the unchanged old runner: held-pipe worker starvation failed, large-output starvation failed, and fragmented BOM decoding failed. No skips. |
| Demonstrated root cause | Windows Process constructs synchronous anonymous-pipe FileStreams. ReadToEndAsync performs blocking native reads on shared pool workers. Declaring the caller async does not make those reads nonblocking. |
| Repair | Shared test runner consumes only bytes PeekNamedPipe reports available; empty pipes await a cancellable delay. Each handle has one reader, with no pending native read during a peek. Bypass FileStream prefetch so available-byte probes cannot miss buffered bytes. |
| Scheduling fairness | Each read yields through the global queue before consuming bytes. Continuously available output cannot prevent deadline installation or monopolize the sole worker ahead of the other stream and exit/deadline callbacks. |
| Encoding repair | Buffer the initial four bytes or EOF before StreamReader detects its BOM. Preserve the original default encoding, CRLF, split UTF-8 characters, UTF-16 BOMs and final text without a newline. Unix keeps its native asynchronous reads through the same prefix adapter. |
| Ownership | Process retains ownership of both handles. Existing runner awaits both readers before disposal; parent/tree termination and caller-cancellation classification remain unchanged. |
| Mutation prerequisite check | Per-test service graph owns workspace manager, preview capacity, undo state and watchers. Scope disposal closes the explicit owning manager; cleanup closes remaining workspaces, disposes provider, then deletes copied roots. No new detached process or read owner is introduced. |
| Deadlines | Existing 30-second verifier and Git deadlines, 10-second teardown budget, inherited-pipe safety deadlines and all original assertions unchanged. No retries, opt-outs or suppressions. |
| Future failure evidence | Timeout includes phase, native exit state, both reader statuses, pool thread count and queued work captured before cleanup. |
| Remaining separate defect | scorecard-script-process-ownership tracks the separate synchronous parent wait and unbounded drain in AggregatePromotionScorecardsScriptTests. |

## Regression coverage

| Case | Contract |
|---|---|
| RunAsync_LimitedWorkers_DrainsWithoutBlockingOtherProcesses / held-pipes | Exited child completes with exact output and exit code despite another live child's pipes. |
| Same method / large-output | Both 64 KiB streams drain with one available worker; no pipe-capacity deadlock. |
| RunAsync_PreservesSplitMultibyteOutputAndBomEncoding | Fragmented BOMs and characters, exact decoded text on both streams. |
| RunAsync_PreservesUtf32BomAndShortOutputAtEof | Four-byte BOM detection and fewer-than-four output bytes at EOF. |
| Existing PwshScriptRunnerTests | Argument boundaries, nonzero exits, caller cancellation, timeouts, tree termination, inherited descendant pipes, Git consumer and async Git worker regression. |
| Existing VerifyReleaseChildScriptTests | All real verifier control-flow fixture cases, including the originally failing case. |
| Existing mutation/ownership suites | All 33 mutation identities and production fixture ownership checks. |

## Qualification

Final shared-runner focused suite: 13 passed, zero skipped, including all unchanged cancellation deadlines. Current-head local and hosted results are recorded in the implementation PR. Focused measurements do not substitute for the unchanged full local gate or both hosted OSes.

## Runtime sources

- [Windows Process pipe construction](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs).
- [PeekNamedPipe contract](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-peeknamedpipe).
