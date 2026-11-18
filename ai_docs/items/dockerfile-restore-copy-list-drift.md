# dockerfile-restore-copy-list-drift — Dockerfile restore COPY list drifts from RoslynMcp.slnx

**row:** `dockerfile-restore-copy-list-drift` · **pri:** `Medium` · **size:** `M`


## Anchors

- `Dockerfile`
- `RoslynMcp.slnx`

## Acceptance

- [ ] docker build succeeds from a clean checkout.
- [ ] A contract test or verifier fails when the Dockerfile restore inputs drift from the solution.

## Evidence

wc1 static derivation (docker build not run): Dockerfile:15-20,27 hand-synced COPY list builds 5 test projects before publish. (doc-audit 2026-11-17)
