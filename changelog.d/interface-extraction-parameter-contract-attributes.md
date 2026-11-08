---
category: Changed
---

- **Changed:** BREAKING: Interface extraction now preserves `InterpolatedStringHandlerArgument` only when compiler-bound source and generated-contract calls select the same member and ordered handler constructors under the receiving project's C# language version. A different constructor, missing binding or probe error refuses the preview before a token is stored, listing all distinct compiler diagnostic ids. Migration: choose a handler constructor and forwarded arguments compatible with both receiver types, or change the interface boundary, then request a fresh preview. The request and response schemas are unchanged. See ADR 0037. Closes `interface-extraction-parameter-contract-attributes`.
