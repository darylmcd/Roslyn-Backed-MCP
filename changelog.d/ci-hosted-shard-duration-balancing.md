---
category: Added
---

- **Added:** `eng/collect-hosted-shard-timings.ps1`, an offline per-image hosted-shard timing collector that reports wall-time skew separately from summed TRX case duration and fails closed below `-MinimumSamples` (default 5) runs per image, and recorded the resulting shard-weighting decision durably in `CI_POLICY.md`.
