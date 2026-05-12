---
category: Fixed
---

- **Fixed:** `[Description]` text on `string[]`-typed tool parameters (`usings`, `imports`, `projects`, `changedFilePaths`) now explicitly states "Pass as a native JSON array, not a JSON-encoded string" with a concrete example, preventing LLM clients from mis-encoding array values as stringified JSON.
