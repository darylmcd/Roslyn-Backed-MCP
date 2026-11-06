---
category: Changed
---

- **Changed:** Interface extraction (`extract_interface_cross_project_preview`, `dependency_inversion_preview`, `extract_and_wire_interface_preview`) now refuses, before any preview token is stored or file written, a type whose emitted members contain pointer or function-pointer signature types when the receiving project does not enable `AllowUnsafeBlocks` (previously a token for an interface failing with CS0227 was issued). The refusal message contains "requires unsafe signatures; receiving project does not enable AllowUnsafeBlocks" and never changes the receiving project's policy. Migration: enable `AllowUnsafeBlocks` in the receiving project. Unsafe-enabled receivers are unchanged: each pointer-bearing member carries its own `unsafe` modifier. Closes `interface-extraction-unsafe-signature-context`.
