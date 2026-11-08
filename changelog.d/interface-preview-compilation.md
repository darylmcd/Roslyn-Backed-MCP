---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Cross-project interface extraction, dependency inversion and extract-and-wire now refuse a preview when its final pending solution introduces compiler errors in changed projects or their dependents. Existing compiler errors do not alone block a preview. Extract-and-wire composes interface extraction and DI registration edits in the same file, qualifies the generated interface in registration calls, adds valid contract references to linked project owners, and refuses cycles, divergent linked edits or unmapped changed C# documents. Migration: make signature metadata available to the receiver, preserve concrete behavior needed by consumers, and use a valid registration call before requesting a new preview. See ADR 0038. Part of `cross-project-interface-signature-compile-gate` and `extract-and-wire-same-file-composition`.
