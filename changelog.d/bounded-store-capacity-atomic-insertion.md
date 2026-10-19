---
category: Fixed
---

- **Fixed:** Enforce preview-store capacity atomically, preserve reflected provider cancellation, notify later change subscribers after handler failures, and reject partial consumer analysis on cancellation. Closes `bounded-store-capacity-atomic-insertion`, `feature-provider-constructor-cancellation-unwrapping`, `change-tracker-subscriber-failure-isolation`, and `consumer-analysis-cancellation-returns-partial`.
