---
category: Changed
---

- **Changed:** Experimental `extract_interface_cross_project_preview`, `dependency_inversion_preview` and `extract_and_wire_interface_preview` now refuse, before storing any preview token, an extraction whose nullable contract the receiving project's compiler cannot represent (for example a nullable-enabled source type extracted into a C# 7.3 / netstandard2.0 receiver, reported as CS8370); these previews previously succeeded with an oblivious interface that silently dropped the non-null contract. Oblivious source members still extract unchanged. Migration: raise the receiving project's `LangVersion` to 8.0 or later, or extract from an oblivious (`#nullable disable annotations`) source. Closes part of `interface-extraction-nullable-context-projection`.
