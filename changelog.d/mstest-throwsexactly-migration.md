---
category: Maintenance
---

- **Maintenance:** Migrated the test suite off MSTest APIs that newer MSTest analyzers promote to build errors under warnings-as-errors: `Assert.ThrowsExceptionAsync` → `Assert.ThrowsExactlyAsync` (129 sites), `Assert.ThrowsException<T>` → `Assert.ThrowsExactly<T>` (28 sites), `[DataTestMethod]` → `[TestMethod]` (4 sites), and strengthened three tautological assertions flagged by MSTEST0032 (value-tuple/`JsonElement` `IsNotNull` checks that were always true now assert the real found/shape condition). Identical exact-type-match semantics at the current MSTest 3.8.3 pin; unblocks every open dependabot MSTest bump (MSTEST0039/MSTEST0044/MSTEST0032/CS0117 failures).
