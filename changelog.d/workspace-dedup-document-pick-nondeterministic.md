---
category: Maintenance
---

- **Maintenance:** `WorkspaceLoadDedupTests.FindWorkspaceIdsContainingFile_UsesLoadedDocumentMembership` picked its probe document with `EnumerateFiles(root, "*.cs", AllDirectories).First()`, which has no defined result over an unordered enumeration. The fixture copy excludes only `bin` — it cannot exclude `obj`, since MSBuildWorkspace needs `obj/project.assets.json` to load — so the tree carries generated sources under both `obj/Debug` and `obj/Release`, and only the active configuration's are compilation documents. Landing on the other returned zero owners and failed as "Different number of elements", reliably on the Linux CI shard and never on Windows. The probe now excludes build-output directories and orders the candidates, so it selects a real project source file identically on every filesystem.
