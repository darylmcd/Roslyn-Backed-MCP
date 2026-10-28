---
category: Fixed
---

- **Fixed:** Both interface extractors (`extract_interface_preview`, `extract_interface_cross_project_preview`, dependency inversion and extract-and-wire) now project each member from its own declaration through one shared `InterfaceMemberProjector`, so ref / ref readonly returns, indexers, generic parameters with constraints, events, `init` and public-only accessors, escaped identifiers, pointer types, parameter modifiers/defaults/attributes, nested and substituted generic type names and nullable annotations carry over instead of producing interfaces the source type cannot implement. Closes `interface-member-projection-shared-emitter`.
