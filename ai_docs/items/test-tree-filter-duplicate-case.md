# test-tree-filter-duplicate-case — Consolidate identical tree-filter test cases

**row:** `test-tree-filter-duplicate-case` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TreeNodeFilterTranslatorTests.cs:24`
- `tests/RoslynMcp.Tests/TreeNodeFilterTranslatorTests.cs:230`

## Acceptance

- [ ] Retain one clearly named regression for the default-null known-name path; remove only the exact duplicate input/call/assertion.
- [ ] Preserve separate empty-known-names, provided-names, equals/contains, version and invalid-filter cases.
- [ ] Run the complete translator class and document the intentional one-case discovery reduction.

## Evidence

- Two methods invoke Translate with the same FQN and null version and assert the same four-segment result; comments are the only body difference.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
