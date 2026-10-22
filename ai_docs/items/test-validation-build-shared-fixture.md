# test-validation-build-shared-fixture — Move the validation build case onto an owned fixture

**row:** `test-validation-build-shared-fixture` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ValidationToolsIntegrationTests.cs:9`

## Acceptance

- [ ] Run BuildProject_SampleLib_Returns_Result against an owned copy and close/delete it deterministically.
- [ ] Keep every build-result assertion and the production build tool path.
- [ ] Re-audit the remaining cases and remove class serialization only after concurrent proof with fixture-copying suites on both OSes.

## Evidence

- Class comment and code identify one real build rewriting the shared sample obj tree as the reason the entire class remains serialized.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
