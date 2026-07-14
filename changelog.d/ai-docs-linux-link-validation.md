---
category: Fixed
---

- **Fixed:** The weekly hosted-ubuntu CI lane (coverage + artifact freshness) had been failing silently since 2026-05-18 at `verify-ai-docs.ps1`: a literal `](...)` ellipsis link in a skill doc resolves under Windows path normalization (so self-hosted PR runs passed) but is a plain missing filename on Linux. The doc now uses the `{braces}` placeholder convention, and the verifier flags all-dot link targets on every platform so PR CI catches this class before the weekly run does.
