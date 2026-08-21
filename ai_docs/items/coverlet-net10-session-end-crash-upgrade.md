# coverlet-net10-session-end-crash-upgrade — Adopt the stable Coverlet teardown fix

**row:** `coverlet-net10-session-end-crash-upgrade` · **pri:** `Medium` · **size:** `S`

## Anchors

- `Directory.Packages.props`

## Acceptance

- [ ] Upgrade to the first stable `coverlet.collector` release containing the registered atomic teardown that replaces AppDomain assembly scanning.
- [ ] The Windows .NET 10 coverage gate completes without a testhost crash and emits valid Cobertura output.
- [ ] Windows Event Log contains no matching `testhost.exe` access violation for the verification window.

## Evidence

- Coverlet 10.0.1 crashed in `CoverletInProcDataCollector.GetInstrumentationClass` on Windows/.NET 10 during the 2026-08-21 gate; identical local event-log stacks predate this branch.
- Upstream PR 1987 removes that assembly-scanning teardown path but was not available in a stable package during this remediation.
