### Fixed

- `get_namespace_dependencies` response now includes `analyzedProjectCount` and `totalNamespacesScanned` so callers can distinguish "no cycles" from "no analysis". Closes [#615](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/615). ([#638](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/638))
