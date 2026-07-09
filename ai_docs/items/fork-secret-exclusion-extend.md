# fork-secret-exclusion-extend — Extend fork secret-file exclusion beyond filename-shape denylist

**row:** `fork-secret-exclusion-extend` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:270`

## Acceptance

- [ ] Common non-matching secret conventions (`config.local.yaml`, `credentials.txt`, `*.p12`, `id_rsa`) are evaluated for exclusion
- [ ] Decision on denylist-extension vs allowlist model recorded

## Evidence

- `IsSecretBearingFile` documents that non-matching secret filenames still copy into the server-writable fork; no tracking row existed for the gap — see code-quality review, PR #1036.
