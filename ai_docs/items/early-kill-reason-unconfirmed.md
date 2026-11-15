# early-kill-reason-unconfirmed — Report early termination only after it occurs

**row:** `early-kill-reason-unconfirmed` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:245-259`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:333-350`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:204-218`

## Acceptance

- [ ] Report an early-kill reason only when the live child was terminated by that attempt, including correct behavior after exit, on kill failure, and for concurrent stream matches. Preserve the underlying file-lock classification and process ownership; prove a real successful early kill and both false-positive controls.

## Evidence

- Base 52f2c418: the coordinator assigns KillReason before TryKillProcessTree, which catches all kill failures; post-exit draining also scans. DotnetOutputParser interprets the reason as proof that the command terminated early.

## Context

- Qualify stable test_run before selection. Measure whether the complete correction changes execution.earlyKillReason, failureEnvelope.summary, or execution.succeeded on both supported protocol eras. If incompatible, defer to the next operator-approved major with ADR, migration, and raw-wire coverage.
