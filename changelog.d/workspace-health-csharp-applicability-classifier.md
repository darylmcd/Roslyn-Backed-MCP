---
category: Fixed
---

- **Fixed:** Fixed `workspace-health` skill misclassifying non-C# repos: the skill now probes CWD for `.csproj`/`.sln`/`.slnx` at startup and emits `applicable: bool` + `detectedStack` in the report header. When `applicable: false`, the "consider loading a workspace" hint is suppressed, preventing the misleading stack-guess that occurred when globally-registered MCP servers for other languages were visible in the tool catalog.
