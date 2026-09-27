---
category: Fixed
---

- **Fixed:** Tool calls with unrecognized argument names now report them in `_meta.unknownArguments`, each with a closest-name `suggestion` when one is within two edits. Previously a typo such as `severty` was silently dropped by the SDK binder. The call is not rejected; the field is additive and omitted when every name is recognized. Closes `unknown-tool-arguments-silently-ignored`.
