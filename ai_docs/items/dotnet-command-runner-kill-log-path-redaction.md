# dotnet-command-runner-kill-log-path-redaction — Redact command-runner kill-failure logs

**row:** `dotnet-command-runner-kill-log-path-redaction` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`
- `tests/RoslynMcp.Tests/HardeningBehaviorTests.cs`

## Acceptance

- [ ] Remove raw target paths and exception objects from process-tree kill-failure logs.
- [ ] Retain stable process id, controlled kill reason, and exception-type fields without exception messages or stack payloads.
- [ ] Preserve cancellation semantics and add a regression proving a secret-shaped target path and exception message are absent from captured logs.

## Evidence

- Adjacent review of the nested Git child-process fix found `TryKillProcessTree` logs both `TargetPath` and the full exception object at Warning level; either can disclose workspace paths or provider-owned failure prose through the enabled ILogger file sink.
