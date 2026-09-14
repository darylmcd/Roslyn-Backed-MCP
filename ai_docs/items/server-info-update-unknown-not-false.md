# server-info-update-unknown-not-false — Report update availability as unknown while the check is pending

**row:** `server-info-update-unknown-not-false` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`
- `src/RoslynMcp.Core/Models/ServerToolDtos.cs`
- `skills/update/SKILL.md`
- `hooks/hooks.json`
- `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs`

## Acceptance

- [ ] `ServerUpdateInfoDto.UpdateAvailable` is `bool?`; `BuildUpdateInfo` emits `null` unless `LastCheckStatus` is `Succeeded`.
- [ ] `checkStatus` of `pending`, `neverChecked`, `failed`, or `timedOut` all serialize `updateAvailable: null`, never `false`.
- [ ] `succeeded` + no newer registry version still serializes `updateAvailable: false`; `succeeded` + strictly-newer version still serializes `true` with `latest` and `command` populated (existing `server-info-update-latest-inverted` contract unchanged).
- [ ] `skills/update/SKILL.md` Step 1 reads the tri-state: `null` means the check has not completed. Its current "If `update` is `null`, the NuGet check hasn't completed yet" line is corrected — `ServerTools.cs:151` always emits the `update` block, so that guidance is already unreachable.
- [ ] The `hooks/hooks.json` prompt hook gains an explicit `null` branch (stay silent / report "check pending"); today it branches only on `true` and on `update` null / `updateAvailable` false, so `null` matches neither.
- [ ] Regression coverage for the pending case added alongside the existing inverted/newer cases.
- [ ] Wire-shape change (non-nullable `bool` → nullable) recorded as a migration note in the changelog fragment: `server_info` is a published-surface contract and `ServerSurfaceCatalog` diffs `outputSchema` per tool (`ServerSurfaceCatalog.cs:417`), so the change is visible on `roslyn://server/catalog-diff`.
- [ ] The embedded historical v2.3.1 catalog snapshot (which pins `updateAvailable` as a required boolean) is NOT edited — it is a frozen v2.3.1 snapshot; the resulting diff entry is the intended output.

## Evidence

- Ten `server_info` probes across 5 release-operational Claude sessions returned `"updateAvailable":false` with `"checkStatus":"pending"`; one session treated that as authoritative and skipped the update, others fell back to hand-querying the NuGet feed. `BuildUpdateInfo` computes `updateAvailable` purely from `latestVersion is not null && latestParsed > currentParsed`, and the first probe of every process is always pending because `GetLatestVersion` only starts the fetch on first access (`NuGetVersionChecker.cs:195-199`, read for root-cause confirmation — no edit needed there).
- `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — finding `server-info-update-unknown-not-false` (§4.8; 2a#server_info-update-check-pending-reports-no-update).
