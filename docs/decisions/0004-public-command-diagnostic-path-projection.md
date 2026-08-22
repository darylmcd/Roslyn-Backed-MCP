# ADR 0004 — Public command-diagnostic path projection

- **Status:** Accepted (2026-08-22)
- **Deciders:** repository maintainers
- **Scope:** stable build/test response text derived from child-process diagnostics
- **Supersedes:** implicit retention of child-emitted absolute paths
- **Superseded by:** nothing

## Context

The public command projection replaced absolute paths that were already known execution inputs, such
as the working directory, target, and results directory. A child process could independently emit a
different absolute source or artifact path in stdout, stderr, early-kill reasons, failure-envelope
text, or TRX-derived test failure details. Those paths bypassed the known-input replacement list.

Retaining them provided useful compiler locations, but also disclosed workstation and network-share
topology. The same text can contain Windows, UNC, or POSIX paths regardless of the host operating
system, so `System.IO.Path` behavior for only the current host is not a sufficient wire policy.

## Decision

1. Public command-diagnostic text recognizes Windows drive, UNC, and POSIX absolute filesystem
   paths using platform-neutral lexical rules.
2. A recognized path lexically contained by the command working directory becomes a `/`-normalized
   relative path. A path outside that directory becomes the stable `<external-path>` placeholder.
3. Compiler `(line,column)` and stack `:line N` suffixes remain after projection. URI and ratio-like
   text are not classified as filesystem paths.
4. The policy applies uniformly to command stdout, stderr, early-kill reasons, failure-envelope
   summaries and tails, and `TestFailureDto.Message` / `TestFailureDto.StackTrace`.
5. Projection occurs only at the public serialization boundary. Internal command and test-run DTOs
   remain unchanged for parsing and server-side diagnosis.

## Compatibility and migration

This is a breaking security correction under `docs/release-policy.md`. It intentionally omits the
normal deprecation window because retaining absolute child-emitted paths discloses sensitive local
filesystem detail.

Clients must stop parsing absolute paths from build/test diagnostic text. Use the retained relative
path plus line/column suffix for workspace navigation. Treat `<external-path>` as an opaque marker:
the referenced file is outside the command workspace and its absolute location is not public.

## Consequences

**Positive**

- Public build/test responses no longer disclose arbitrary local or network absolute paths.
- In-workspace compiler locations remain useful and portable across host operating systems.
- All public test failure channels follow one policy while internal diagnostic data stays intact.

**Negative**

- Clients can no longer open external files by extracting their absolute path from command output.
- Conservative text recognition may replace filesystem-shaped text even when a child process meant
  it only as prose; the stable placeholder keeps that behavior deterministic.

## References

- `docs/product-contract.md` — stable public diagnostic-path contract
- `docs/release-policy.md` — security-correction and deprecation policy
- `docs/decisions/0003-sdk-2x-wire-compatibility.md` — public secret-safe wire posture
- `src/RoslynMcp.Host.Stdio/Tools/TestRunPublicProjection.cs` — boundary implementation
- `tests/RoslynMcp.Tests/TestRunPublicProjectionTests.cs` — cross-platform regression matrix
