# logging-wire-test-fixed-delay — Replace the fixed Task.Delay in the logging lifecycle wire test

**row:** `logging-wire-test-fixed-delay` · **pri:** `Low` · **size:** `S` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `tests/RoslynMcp.Tests/McpLoggingLifecycleWireTests.cs:62`

## Acceptance

- [ ] `McpLoggingLifecycleWireTests.cs:62` (`await Task.Delay(TimeSpan.FromMilliseconds(100))` before `Assert.IsEmpty(stdout.Snapshot(), ...)`) is replaced by a deterministic condition; the negative assertion no longer relies on a fixed sleep.
- [ ] Use the existing bounded poll helper `WaitForConditionAsync` (`McpLoggingLifecycleWireTests.cs:246`) or an explicit signal; check the other fixed delay at `:265` for the same pattern.

## Evidence

- A fixed delay as the sync for a negative assertion passes vacuously on a slow host and wastes time on a fast one. The same file already polls for the sweep effect with `WaitForConditionAsync` (`:68`).
