---
category: Fixed
---

- **Fixed:** Interface extraction, dependency inversion and extract-and-wire preserve parameter modifiers, optional defaults, caller metadata, nullable types, escaped identifiers, varargs and ref returns; indexers retain their parameters and public accessors, including init and reference escape contracts. Existing generic context, class constraints and header comments remain valid when adding the interface base type. Unsafe signatures retain their context when target policy permits it; incompatible unsafe policy or handler constructor bindings are refused before preview publication. Closes `cross-project-interface-parameter-semantics`, `interface-extraction-ref-return-contracts`, `interface-extraction-unsafe-signature-context`, `interface-extraction-escaped-member-identifiers`, and `interface-extraction-base-constraint-token-boundary`.
