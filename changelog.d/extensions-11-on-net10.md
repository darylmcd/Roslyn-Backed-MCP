---
category: Changed
---
- **Changed:** Move the Microsoft.Extensions runtime family (Hosting, Http, Logging, Logging.Console) from 10.0.14 to 11.0.0. The target framework stays net10.0 and the runtime prerequisite is unchanged; 11.x copies of some shared-framework libraries such as System.Text.Json now ship with the server. See ADR 0040.
