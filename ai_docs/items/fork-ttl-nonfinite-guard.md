# fork-ttl-nonfinite-guard — Guard env-configured fork TTL/timeout against non-finite values

**row:** `fork-ttl-nonfinite-guard` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:165`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:217`

## Acceptance

- [ ] `ReadPositiveEnvDouble` rejects non-finite (`Infinity`/`NaN`) parsed values and falls back to the default
- [ ] `SweepExpiredForks` cannot throw `OverflowException` from `TimeSpan.FromHours` when `ROSLYNMCP_FORK_TTL_HOURS` is set to a non-finite string

## Evidence

- `double.TryParse(raw, NumberStyles.Float, ...)` accepts `"Infinity"`; `+Infinity` passes the `parsed>0` check and `TimeSpan.FromHours(+Infinity)` in `SweepExpiredForks` throws `OverflowException` on the fork-apply write path — see `ai_docs/plans/20260709T033118Z_backlog-sweep/plan.md` (workspace-fork-apply-security-hardening code-quality review, PR #1036).
