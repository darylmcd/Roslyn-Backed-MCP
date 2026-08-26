---
category: Fixed
---

- **Fixed:** Made the script-evaluation tests tolerate worker-process spawn latency under parallel CI load — outcome-asserting tests now use a spawn-tolerant script budget instead of a five-second one, the cooperative-cancellation test accepts either correct terminal timeout shape, and every harness timeout in the suite now exceeds the script budget it supervises instead of cutting it short.
