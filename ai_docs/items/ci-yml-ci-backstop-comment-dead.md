# ci-yml-ci-backstop-comment-dead — Delete the dead ci-backstop declaration in ci.yml

**row:** `ci-yml-ci-backstop-comment-dead` · **pri:** `Low` · **size:** `S` · **verified:** `f903fb6c1b3f5b94d38b6a970e17796b58ed6c8e`


## Anchors

- `.github/workflows/ci.yml`

## Acceptance

- [ ] ci.yml carries no unconsumed backstop declaration; workflow contract tests green.

## Evidence

CI_POLICY split agent searched the repo and ~/.claude for a consumer and found none; the policy bullet was already removed. (doc-audit 2026-11-17)
