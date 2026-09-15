---
category: Fixed
---

- **Fixed:** Experimental validation bundles now report `compile-incomplete` when compilation was cancelled or did not finish every selected project, unless a diagnostic or test failure takes precedence. Migration: treat this status as non-passing and retry compilation; zero errors alone do not establish a clean result. See [ADR 0010](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/docs/decisions/0010-validation-verdict-completeness.md). Closes `workspace-validation-incomplete-compile-verdict`.
