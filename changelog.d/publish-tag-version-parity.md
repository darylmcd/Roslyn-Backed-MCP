---
category: Fixed
---

- **Fixed:** the publish workflow now fails before build, pack, and push when the pushed tag or published GitHub release name disagrees with the canonical package version, closing the mistag path that could publish mismatched MCP-registry provenance or silently skip a duplicate NuGet package. Manual `workflow_dispatch` dry runs continue to validate against the repository version without a tag.
