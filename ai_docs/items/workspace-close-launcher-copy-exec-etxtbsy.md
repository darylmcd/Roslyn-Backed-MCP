# workspace-close-launcher-copy-exec-etxtbsy — Avoid Linux write-then-execute races in drain fixtures

**row:** `workspace-close-launcher-copy-exec-etxtbsy` · **pri:** `Medium` · **size:** `S` · **verified:** `fd50e27b0f0388b3a5c6c24b6d8976c4cc851141`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs:1194-1231`

## Acceptance

- [ ] Linux launcher fixture starts an executable under each isolated workspace path without a copy-then-execute writer-descriptor race; preserve the process-path identity that drain tests exercise.
- [ ] Cover every caller of the shared launcher helper, including cancellation, resolver and sibling-process cases. Keep Windows behavior intact.
- [ ] Verify the fix on hosted Linux and a base-commit control. Handle a separate temporary filesystem without skipping or weakening a test; if the platform cannot provide the required process-path identity, fail with a specific explanation.

## Evidence

- Hosted PR #2042 job `validate-leg (linux-3-of-4)` on head `7a20983a7933420730dde1de0315c123cd9b3657` failed `CloseWorkspace_DrainBudgetExhausted_ReportsInDirTesthostItLeftRunning`: `Process.Start` of a unique copied `/bin/sleep` returned `Text file busy` before drain logic (2026-11-09). Exact-base dispatch #42662924295 at `fd50e27b0f0388b3a5c6c24b6d8976c4cc851141` was started for comparison; its outcome is pending at filing.
- The shared helper copies `/bin/sleep`, changes mode, then starts it. The .NET runtime issue https://github.com/dotnet/runtime/issues/58964 documents ETXTBSY when an in-flight fork retains a writer descriptor after the parent closes it; this is a plausible mechanism, not a proven process attribution here.
