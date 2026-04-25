---
category: Fixed
---

- **Fixed:** `scaffold_test_preview` sibling-inference is now scoped to file-scoped namespace + base-class shape only. `IsFrameworkClassAttribute` blocklist extended with `[Trait]` / `[Category]` / `[TestCategory]` (joining the existing `[TestClass]`/`[TestFixture]`/`[Collection]`); `ExtractPatternFromSource` accepts an optional `Compilation` and uses the test project's `SemanticModel` via `TrimUsingsToReferencedNamespaces` to drop unreferenced sibling usings — eliminating the 10+-unused-import leak when the MRU sibling was a Playwright/Selenium fixture (`scaffold-test-preview-sibling-inference-overbroad`).
