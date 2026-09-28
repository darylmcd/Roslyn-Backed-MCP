# server-info-update-available-next-major-nullable — at the 5.0 cut, make server_info.update.updateAvailable nullable

**row:** `server-info-update-available-next-major-nullable` · **pri:** `Defer` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/ServerToolDtos.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`
- `hooks/hooks.json`
- `skills/update/SKILL.md`
- `docs/product-contract.md`
- `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs`
- `tests/RoslynMcp.Tests/ServerInfoUpdateWireContractTests.cs`

## Acceptance

- [ ] Start only at the 5.0.0 cut (operator decision 2026-09-28: additive in 4.x, contract change at 5.0).
- [ ] `ServerUpdateInfoDto.UpdateAvailable` is `bool?`: `null` unless `checkStatus` is `succeeded`; `latest` and `command` populate only when it is `true`.
- [ ] `ServerInfoUpdateWireContractTests` re-pins the `update` schema with `updateAvailable: ["boolean","null"]` in both protocol eras, plus the per-status wire values.
- [ ] The `server_info` hook prompt and the `/roslyn-mcp:update` skill treat `null` as unknown; `docs/product-contract.md` drops this field's 5.0 deprecation row.
- [ ] ADR 0011 (`v5-major-release-contract-prereqs`) records old→new wire behavior, and the `Changed — BREAKING` fragment carries the consumer migration note.

## Evidence

- Retro 2026-09-13 (`server_info-update-check-pending-reports-no-update`): 10 probes in 5 sessions read `updateAvailable:false` from an unfinished check — see `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`.
- #1553 (`77c1055d`) widened the type inside 4.x; branch `fix/additive-4x-breaking-rework` restored the v4.2.1 boolean and made `checkStatus` the documented "unknown" authority.

## Context

4.x documents `checkStatus` + `lastCheckedAt` as the "unknown" signal (`docs/product-contract.md` § Update availability). Widening a stable output-schema field is a major-version change under `docs/release-policy.md`.
