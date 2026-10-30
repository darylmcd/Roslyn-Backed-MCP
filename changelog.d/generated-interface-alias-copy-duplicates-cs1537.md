---
category: Fixed
---

- **Fixed:** Generated interface files now use project-scoped global usings without copying them into the same project, deduplicate required namespaces against source and destination globals, and sort only `System` namespaces first. Closes `generated-interface-alias-copy-duplicates-cs1537`, `generated-interface-global-using-dedup-cs8933`, and `using-directive-synthesizer-sort-hygiene`.
