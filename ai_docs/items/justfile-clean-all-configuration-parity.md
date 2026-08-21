# justfile-clean-all-configuration-parity — Clean Debug and Release outputs

**row:** `justfile-clean-all-configuration-parity` · **pri:** `Low` · **size:** `S` · **deps:** `justfile-powershell-recipe-portability,local-ci-informational-coverage-separation`

## Anchors

- `justfile`
- `tests/RoslynMcp.Tests/JustfilePortabilityTests.cs`

## Acceptance

- [ ] `clean-all` removes generated build products from both Debug and Release configurations before deleting `artifacts`.
- [ ] The recipe remains idempotent when either configuration or `artifacts` is absent on Windows and POSIX shells.
- [ ] One integration-shaped regression creates distinct Debug and Release sentinels, invokes `clean-all`, and proves both are removed.

## Evidence

- After the 2026-08-21 release gate, `just clean-all` invoked `dotnet clean RoslynMcp.slnx` without a configuration and removed only Debug outputs; a separate `dotnet clean -c Release` then removed hundreds of surviving Release products.
- `justfile-powershell-recipe-portability` already owns overlapping shell-invocation edits, so this row sequences after it instead of racing the same recipe.
