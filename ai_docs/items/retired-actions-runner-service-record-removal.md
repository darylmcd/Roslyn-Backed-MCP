# retired-actions-runner-service-record-removal — Delete the orphaned privileged service

**row:** `retired-actions-runner-service-record-removal` · **pri:** `High` · **size:** `S`

## Anchors

- `docs/self-hosted-runner.md`

## Acceptance

- [ ] From an elevated session, stop, disable, and delete `actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev`; fail on command errors and prove the service record is absent.
- [ ] Prove `C:\Users\daryl\actions-runner` remains absent, preserve any required diagnostics, resolve `C:\Users\daryl\actions-runner.retired-20260824` exactly, and remove only that quarantined directory.
- [ ] After a reboot boundary, prove the repository runner API still reports zero runners and no retired runner process, service, original path, or quarantine remains.
- [ ] Record one fail-closed evidence block covering the service, process, both exact paths, and repository API before closing this row.

## Evidence

Runner id 22 was removed from `darylmcd/Roslyn-Backed-MCP`; the repository API reported zero runners and the service stopped. After verifying zero runner processes and an absent destination, the installation moved recoverably to `C:\Users\daryl\actions-runner.retired-20260824`, leaving the configured executable path absent. The service record remains `Auto` and `LocalSystem`; a writable configured path could be recreated before reboot, so elevated service deletion remains security work rather than optional filesystem hygiene.
2026-08-24 partial cleanup: after revalidating zero repository runners, zero retired-runner processes, the exact stopped service identity/path, an absent original root, and both expected dangling junction targets, removed the two junctions and the 1.21 GiB quarantine. The non-elevated session could not delete the stopped delayed-auto LocalSystem service; harden docs/self-hosted-runner.md around process/reparse-point checks and retain the elevated deletion plus post-reboot proof before closure.
