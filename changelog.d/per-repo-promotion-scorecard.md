### Changed

- Promotion scorecards are now per-audited-repo (`<repo>/ai_docs/audit-reports/_latest-promotion-scorecard.json`) instead of a single last-write-wins file at `<Roslyn-MCP-root>/...`. `/publish-preflight` Step 8 aggregates scorecards from configured sibling repos via the new `eng/aggregate-promotion-scorecards.ps1` and applies a quorum rule (≥2 workspaces with `promote`, no blockers) before recommending a tier flip. Single-workspace anomalies no longer drive tier decisions. `/promote-tier` accepts the aggregated input format. Closes `per-repo-promotion-scorecard`.
