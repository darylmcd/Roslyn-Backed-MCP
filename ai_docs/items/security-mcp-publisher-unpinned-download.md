# security-mcp-publisher-unpinned-download — Pin and checksum-verify the mcp-publisher download in publish-registry

**row:** `security-mcp-publisher-unpinned-download` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/workflows/publish-nuget.yml:199`
- `tests/RoslynMcp.Tests/PublishWorkflowContractTests.cs:15`

## Acceptance

- [ ] `publish-nuget.yml` downloads mcp-publisher from a fixed release tag URL, never `releases/latest`.
- [ ] The archive SHA-256 is verified before extraction; a mismatch fails the job before `mcp-publisher login github-oidc`.
- [ ] A contract test in `PublishWorkflowContractTests` fails when the workflow contains `releases/latest` or omits the hash check (red on the current workflow before the fix).
- [ ] The pinned version and hashes sit where a maintainer bumps them, with the bump steps named next to them.

## Evidence

- `.github/workflows/publish-nuget.yml:196-211` pipes `releases/latest/download/mcp-publisher_*.tar.gz` into `tar`, then runs it after OIDC login in a job with `id-token: write` (:145-147).
- `eng/verify-actionlint.ps1:255-285` already pins version and verifies archive and binary SHA-256: the repo knows the control.
- refactor-audit 20261028-1500 at f8734a2f, finding `security-mcp-publisher-unpinned-download` (fingerprint 9fb757fb0f); tier 0 (workflow read), no job run.

## Context

- regression_shape: contract test greps the publish workflow for the unpinned download and for the missing hash step.
- Counterargument weighed: official org binary over TLS and the registry docs recommend this command; cost is a bump chore Dependabot cannot track. The job is the only one holding the OIDC identity for the io.github.darylmcd/* namespace.
- `gh api .../actions/secrets` lists NUGET_API_KEY only; the registry job needs no secret, so OIDC is the asset at risk.
