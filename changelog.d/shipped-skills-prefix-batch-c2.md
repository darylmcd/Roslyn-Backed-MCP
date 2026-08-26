---
category: Fixed
---

- **Fixed:** the shipped `version-bump` and `workspace-health` skills now resolve the Roslyn MCP tool prefix once from a `server_info` response shape and pin it, instead of instructing a hard-coded `mcp__roslyn__` literal — marketplace-plugin installs (which surface `mcp__plugin_roslyn-mcp_roslyn__*`) no longer halt on a prefix they never expose. This completes the shipped-skills sweep: `residualUnsweptAllowlist` in `eng/banned-skill-markers.json` is now empty and its shrink ratchet is pinned at 0, so `eng/verify-skills-are-generic.ps1` enforces the prefix-agnostic rule and canonical-block byte-identity across every shipped skill with zero amnesty.
