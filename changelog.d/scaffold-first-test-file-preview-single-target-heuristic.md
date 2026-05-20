---
category: Fixed
---

- **Fixed:** `scaffold_first_test_file_preview` failing with "Multiple test projects reference" when several test projects transitively reference a domain library but only one follows the `<Library>.Tests` naming convention. The service now applies a name-suffix tiebreaker (case-sensitive `StringComparison.Ordinal`) and selects the unambiguous candidate automatically. Variants like `MyLib.UnitTests` or `MyLib.IntegrationTests` still require explicit `testProjectName` — the heuristic is conservative by design. Fixes gh #768 §13.18.