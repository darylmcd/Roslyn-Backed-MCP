---
category: Fixed
---

- **Fixed:** `test_related` and `test_related_files` now key discovered tests by owning project plus fully-qualified name, so equal test FQNs in separate test projects stay distinct with the correct `ProjectName`/`FilePath`/`TriggeredByFiles` and pagination total instead of throwing a duplicate-key `ArgumentException` (file route) or collapsing to the first project (symbol route); public DTOs and FQN spelling are unchanged. Closes `test-related-project-qualified-identity`.
