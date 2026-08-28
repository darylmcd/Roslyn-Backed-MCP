---
category: Fixed
---

- **Fixed:** diagnostic details now report incomplete code-fix discovery and secret-safe correlated provider failures while retaining healthy fixes; code-action failure logging has deterministic redaction coverage; the changed-file formatter gate enforces FINALNEWLINE, IDE1006, IMPORTS, and WHITESPACE through one fail-closed grammar; and modifier-specific naming exemptions reduce tracked formatter debt from 418 to 121 findings (IDE1006 306 to 12). Closes `diagnostic-codefix-enumeration-completeness`, `code-action-provider-execution-failure-redaction-coverage`, `changed-format-gate-diagnostic-id-contract`, `format-gate-baseline-generator-shared-grammar`, `changed-format-gate-fail-closed-guard-coverage`, and `editorconfig-private-field-naming-rule-overbroad`.
