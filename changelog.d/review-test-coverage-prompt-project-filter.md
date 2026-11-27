---
category: Fixed
---

- **Fixed:** Filter `TestDiscoveryDto.TestProjects` by `projectName` (ordinal case-insensitive) before flattening and capping test cases in the `review_test_coverage` prompt so the embedded test list and truncation footer reflect only the selected project. Closes `review-test-coverage-prompt-project-filter`.
