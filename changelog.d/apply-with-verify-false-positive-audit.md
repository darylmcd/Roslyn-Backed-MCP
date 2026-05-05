---
category: Maintenance
---

- **Maintenance:** audited `apply_with_verify` rollback false-positive rate against the multi-session retro's claim of ~5/36 false positives. Outcome: the retro's premise was incorrect — both `ApplyWithVerifyTool` and `EditService` already use diff-based diagnostic fingerprints (`id|file:line:col|message`) and `HashSet.Except` to filter out pre-existing diagnostics. No implementation change warranted. A real edge case (line-shift fingerprint instability when an apply inserts/deletes lines) is documented in `ai_docs/reports/20260505T131500Z_apply-verify-rollback-audit.md`; a follow-on implementation row will land if/when concrete session evidence surfaces. Closes `apply-with-verify-false-positive-audit`.
