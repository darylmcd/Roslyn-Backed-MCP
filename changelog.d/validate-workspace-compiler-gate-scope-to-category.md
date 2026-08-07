---
category: Fixed
---

- **Fixed:** `validate_workspace`'s compiler-arm corroboration gate (`WorkspaceValidationService.MergeErrorDiagnostics`) now scopes to `Category=="Compiler"` rows only. Previously the gate suppressed the entire `CompilerDiagnostics` harvest arm on an uncorroborated/clean compile pass, which also silently dropped the unrelated `WORKSPACE001` (`Category=="Workspace"`) row — a genuine "failed to load this project's compilation" signal — reporting a false `clean` verdict.
