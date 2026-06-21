---
category: Maintenance
---

- **Maintenance:** Documented the canonical-URL-only assumption in `eng/verify-registry-readiness.ps1` — the `repository-url-matches-name` owner check intentionally accepts only canonical `https://github.com/` repository URLs (mcp-publisher mandates them), rejecting `http://`, `www.`, and `git@github.com:` SCP-style forms by design. Added a comment above the regex so the narrow pattern reads as intentional rather than an oversight (close-as-won't-fix). Closes `registry-readiness-url-regex-canonical-only`.
