---
category: Fixed
---

- **Fixed:** Internal invariant guards (the validate_workspace failure-operation switch default, the file-watcher stale reason, the dotnet runner executable path, and the undo pre-apply solution type) no longer throw caller-blaming `ArgumentException`s that the host redacts to "Parameter 'x' is invalid"; impossible arms throw `UnreachableException` and wiring-contract violations throw `InvalidOperationException`.
