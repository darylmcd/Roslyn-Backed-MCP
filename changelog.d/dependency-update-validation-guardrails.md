---
category: Changed
---

- **Changed:** Raise the repository source-build SDK floor from 10.0.100 to 10.0.400 because the former cannot compile the Roslyn 5.9 analyzer, exercise that exact floor in isolated hosted CI, route contract-sensitive and compile-family dependency updates independently, verify central pins and MCP SDK license attribution, and make audit and readiness fixtures independent of caller or test-order state.
