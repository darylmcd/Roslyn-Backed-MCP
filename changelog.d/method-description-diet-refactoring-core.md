---
category: Maintenance
---

- **Maintenance:** Trimmed method-level `[Description]` text on the refactoring-core tools (`extract_method_preview`, `extract_method_apply`, `extract_shared_expression_to_helper_preview`, `change_signature_preview`, `parameter_object_preview`, `get_syntax_tree`) to ~200-char capability statements, cutting ~1,800 chars (~450 tokens) from every `tools/list` response. Refusal reasons and budget semantics were not lost — they remain in the tools' runtime error messages and parameter descriptions. Added a slice-scoped description-length regression.
