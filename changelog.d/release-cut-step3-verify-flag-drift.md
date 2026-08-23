---
category: Maintenance
---

- **Maintenance:** `/release-cut` Step 3 now runs the publish-gate verify invocation (`eng/verify-release.ps1 -Configuration Release -NoCoverage -RequireConsumedFragments`) instead of the bare form. The bare form is the dispatch/schedule informational coverage path: it gates nothing, trips the known Coverlet .NET 10 teardown crash (`coverlet-net10-session-end-crash-upgrade`) that aborts an otherwise-green run with exit 1 after every test has passed, and omitted `-RequireConsumedFragments` so Step 3 never exercised the breaking-to-major rule it is the only local gate for. The step now documents why each flag is load-bearing, carries a reference table of the PR-merge / publish / informational invocations to catch future drift, and tells the operator to suspect a collector crash when a run reports `Failed: 0` yet exits non-zero. Closes `release-cut-step3-verify-flag-drift`.
