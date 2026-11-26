---
category: Fixed
---

- **Fixed:** Redact native `--treenode-filter` values alongside `--filter` in public test-run responses, including complete literal echoes whose fragments survive server-owned output, TRX message, stack trace, or display-name cuts. Preserve literal child arguments, internal diagnostics, path projection, response fields, counts, and truncation markers. Migration: retain the request filter locally instead of recovering it from public argv or diagnostics; option values publish `<redacted>` and matched diagnostic positions publish `*`. This caller-input removal ships under release-policy Rule 2 and ADR 0044 in 5.x.
