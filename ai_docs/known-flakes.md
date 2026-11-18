# Known flakes

<!-- purpose: Registry of investigated flaky tests, each with base-commit proof and a tracking row; never an override for red CI. -->

Registry of flaky tests that have been investigated and are tracked as defects. An entry never turns a red check green: CI stays red until the test is fixed or the failure is reproduced on the base commit and a root-cause row exists (Standing Directives #1, #7).

**Discipline:**

- Add an entry only in a dedicated PR after triage, with: the *symptom*, the *evidence* (base-commit reproduction), and the tracking backlog row id.
- Remediation sessions MUST NOT add entries on their own during a run.
- Delete an entry in the PR that de-flakes its test.

## Registered flakes

| Test FQN / pattern | First seen | Symptom | Notes |
|---|---|---|---|
| **No active flakes** | — | — | — |
