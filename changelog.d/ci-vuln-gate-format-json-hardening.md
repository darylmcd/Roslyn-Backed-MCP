---
category: Maintenance
---

- **Maintenance:** The CI vulnerability-audit gate now decides on `dotnet package list --vulnerable --include-transitive --format json` (failing on a non-empty `vulnerabilities` array for any top-level or transitive package) instead of substring-matching the English summary line, so an SDK wording change or non-English locale can no longer silently revert the gate to fail-open. dotnet stdout is parsed in isolation from stderr (no `2>&1` into the JSON), and unparseable/errored audits fail closed (`ci-vuln-gate-format-json-hardening`).
