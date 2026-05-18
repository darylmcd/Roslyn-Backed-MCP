---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Fixed cross-tool semantic inconsistency between `find_overrides` and `member_hierarchy.overrides` — both tools now use the canonical definition of "override" (symbols actually marked `override` of a virtual/abstract declaration). Sibling interface implementations (e.g., independent `IDisposable.Dispose` implementations across a solution) are no longer misclassified as overrides; they now appear in the new `member_hierarchy.siblingInterfaceImplementations` bucket. `find_overrides` and `member_hierarchy.overrides` now agree on the same target. Breaking: `symbol_relationships.overrides` also loses sibling interface impls (shared `FindOverridesAsync` fix). Closes [gh #736], [gh #737].
