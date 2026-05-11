### Fixed

- `fix_all_preview(IDE0305)` now gracefully returns a `perOccurrenceFallback` envelope instead of crashing with `FixAllProviderCrash: Sequence contains no elements`. (Same provider crash class previously documented for IDE0300; this initiative confirmed coverage via a regression fixture pinning the IDE0305 envelope shape.) ([#642](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/642))
