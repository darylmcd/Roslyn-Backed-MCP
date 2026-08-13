# startup-diagnostics-surface-identity-parity — Compare exact registered surface identities

**row:** `startup-diagnostics-surface-identity-parity` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/StartupDiagnostics.cs`
- `src/RoslynMcp.Core/Models/ServerToolDtos.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs`

## Acceptance

- [ ] Compare exact selected tool, resource, and prompt names instead of count parity alone.
- [ ] Report deterministically sorted missing and unexpected names in startup diagnostics and `server_info`.
- [ ] Preserve the compact success shape when all three surfaces match.
- [ ] Add one same-count member-swap regression that fails parity with actionable identity details.

## Evidence

- Current parity compares only counts, so replacing one expected registration with a different same-kind member still reports `parity=ok`.
