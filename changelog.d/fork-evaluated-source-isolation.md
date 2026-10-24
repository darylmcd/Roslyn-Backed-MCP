---
category: Fixed
---

- **Fixed:** Experimental workspace forks reject evaluated imports, inputs and restore outputs that reach the source workspace. Native load and build checks also guard package preprocessing, assets-cache writes and copied outputs, including late project references and physical aliases. Retained forks preserve these checks when reopened or tested.
- **Migration:** Forking a retained experimental workspace now creates a sibling in its outermost verified physical fork pool, preventing recursive path growth. Use the returned `forkPath`; do not derive it from the immediate source. The current configured root and any client narrowing must authorize that pool and exact destination. Reopen with an authorized original root when a retained-only boundary refuses the sibling pool. Windows refuses an unlaunchable generated working directory before creating or sweeping storage; move or reopen the source under a shorter authorized root. See [ADR 0029](../docs/decisions/0029-retained-fork-storage-and-lifetime.md).
- **Migration:** Automatic expiry deletes only verified owned fork directories. Unmarked forks from before admission material was introduced, unrelated timestamp-shaped siblings and reparse-point entries are preserved; inspect and remove those manually when no longer needed.
