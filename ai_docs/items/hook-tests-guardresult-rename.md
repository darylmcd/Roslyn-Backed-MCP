# hook-tests-guardresult-rename — Rename GuardResult to a generic process-result type in hook tests

**row:** `hook-tests-guardresult-rename` · **pri:** `Low` · **size:** `S` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `tests/RoslynMcp.Tests/HookConfigurationTests.cs:526`

## Acceptance

- [ ] `GuardResult` (`HookConfigurationTests.cs:526`) is renamed to a generic process-result name; it is returned by `RunHookCommandAsync` (`:379`, `:414`) as well as `RunReleaseManagedGuardAsync` (`:481`, `:518`), so the guard-specific name misleads.
- [ ] All usages updated; the file compiles and `HookConfigurationTests` stay green.

## Evidence

- Introduced when PR 2172 added `Shipped_ApplyReminder_IsNonBlockingCommandHook` (`HookConfigurationTests.cs:87`), which runs a non-guard command through the guard result type.
