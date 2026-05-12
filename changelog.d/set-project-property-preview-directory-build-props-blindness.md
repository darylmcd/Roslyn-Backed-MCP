---
category: Fixed
---

- **Fixed:** `set_project_property_preview` silently generating redundant property entries when the target property is already inherited from `Directory.Build.props`. The tool now inspects the evaluated MSBuild property graph and includes a `warnings` annotation when the requested value is already globally effective.
