# editorconfig-option-null-value-prewrite-validation — Investigate null option values before transaction

**row:** `editorconfig-option-null-value-prewrite-validation` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:345`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:391`
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs:43`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`
- `tests/RoslynMcp.Tests/Helpers/ProductionParityMcpHarness.cs`

## Acceptance

- [ ] Reproduce null value through direct service and both supported raw-wire eras; record old error category/identity and file, snapshot, watcher and rollback state.
- [ ] Trace the failing value dereference and every caller before choosing validation ownership. Reject invalid null input before a write transaction when the runtime contract warrants it.
- [ ] Preserve blank/reset and arbitrary valid option values; do not impose the specialized suppression severity enum on the generic option API.
- [ ] Observe regression fail on base and pass after the complete fix. Record ADR and major migration if stable wire identity/category changes.

## Evidence

- Source re-vet at `c43e8fa6995151b22be6feb2c4a1020263b88ce5`; no runtime reproduction claimed.
- `SetOptionAsync` accepts nonnullable `string value` at line346; only key is validated at lines350-353.
- Local transaction `Write` calls `UpsertKeyAcrossCSharpSections(lines, csharpSection, key.Trim(), value.Trim());` at line391 after path/coordinator resolution and original snapshot capture.
- Direct null input dereferences `value` at that construct; actual Host null binding, mutation and rollback outcomes require reproduction before asserting them.
- Existing section-matching and parallel-section rows address different mechanisms. Original count=15 remediation selection remains unchanged.
