---
category: Fixed
---

- **Fixed:** `get_complexity_metrics` (`filePaths`) and `get_msbuild_properties` (`includedNames`) parameter descriptions now include the "native JSON array" guard phrase, preventing LLM clients from mis-encoding array arguments as stringified JSON (`filepaths-batch-2a-advanced-msbuild`).
