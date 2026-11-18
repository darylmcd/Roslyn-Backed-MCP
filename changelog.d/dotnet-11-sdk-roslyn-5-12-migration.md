---
category: Changed
---
- **Changed:** Raise the repository source-build SDK floor from 10.0.400 to 11.0.100 and move the Roslyn API family to 5.12.0 and the Microsoft.Build compile family to 18.7.1, adding Microsoft.NET.StringTools to that family. The target framework stays net10.0, so the published package still needs only .NET 10; building from source now needs the .NET 11 SDK plus the .NET 10 runtime. See ADR 0039.
