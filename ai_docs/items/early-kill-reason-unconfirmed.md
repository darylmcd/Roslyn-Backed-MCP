# early-kill-reason-unconfirmed — Report early termination only after it occurs

**row:** `early-kill-reason-unconfirmed` · **pri:** `Medium` · **size:** `M` · **verified:** `52f2c418cf5ba982b2a1c0914dd3977655d93eb4`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:227-239`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:313-330`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:204-218`

## Acceptance

- [ ] Report an early-kill reason only when the live child was terminated by that attempt, including correct behavior after exit, on kill failure, and for concurrent stream matches. Preserve the underlying file-lock classification and process ownership; prove a real successful early kill and both false-positive controls.

## Evidence

- Base 52f2c418: the coordinator assigns KillReason before TryKillProcessTree, which catches all kill failures; post-exit draining also scans. DotnetOutputParser interprets the reason as proof that the command terminated early.

## Context

- Leave unselected under 5.x until the release impact of removing the previously observable EarlyKillReason and changing the stable failure summary is resolved. If incompatible, resume at the next operator-approved major with ADR, migration, and raw-wire coverage.
