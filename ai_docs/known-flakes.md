# Known flakes

<!-- purpose: Authoritative list of pre-existing flaky tests that remediation executors and the orchestrator consult when judging "is the build green?". -->

Pre-existing flaky tests that subagents and the orchestrator should ignore when judging "is the build green?". When all failing tests match a registered pattern, the validation step treats the result as success and surfaces the count via `known flakes encountered: N` in the report.

**Discipline** (per `/backlog-remediate`):

- Subagents MUST NOT add new entries themselves — flakes go in via a dedicated PR after triage so the registry reflects real, investigated flakes, not noise.
- The orchestrator may consult this registry to override a subagent's failure verdict, but MUST NOT add entries during a sweep.
- Each entry should name the *symptom*, the *evidence*, and a *fix-or-track* disposition so future readers can decide whether to attempt the underlying fix.
- Delete an entry in the PR that de-flakes its test.

## Registered flakes

| Test FQN / pattern | First seen | Symptom | Notes |
|---|---|---|---|
| **No active flakes** | — | — | — |
