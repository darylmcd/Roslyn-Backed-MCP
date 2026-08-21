# command-output-external-path-public-policy — Define public policy for external paths in command output

**row:** `command-output-external-path-public-policy` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TestRunPublicProjection.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `tests/RoslynMcp.Tests/TestRunPublicProjectionTests.cs`

## Acceptance

- [ ] Define whether arbitrary absolute paths found only in child-process diagnostics become workspace-relative paths, stable redactions, or retained external diagnostics.
- [ ] Apply the policy consistently to stdout, stderr, early-kill reasons, and failure-envelope tails without losing compiler line/column context.
- [ ] One cross-platform fixture covers an in-workspace path, an external path, and lookalike non-path text.

## Evidence

- The public projection now removes every known execution input and root, but intentionally retains unrelated absolute paths emitted only by a child process because no product policy defines a safe useful transformation.
2026-08-20 adjacent review: include TestFailureDto.Message and TestFailureDto.StackTrace in the public path-policy matrix; TRX-derived structured failures can carry absolute source paths independently of execution stdout, stderr, or envelope tails.
