---
category: Maintenance
---

- **Maintenance:** Raised the CI `validate` job's `timeout-minutes` from 25 to 40. The self-hosted Windows runner's suite (now ~1700+ tests after several backlog-sweep rounds) has been observed taking 10-25 minutes even on clean, otherwise-healthy runs — well above the historical ~2-3 minute baseline the old budget assumed, with no single root cause pinned down (a runaway orphaned process and the Windows power plan were both checked and ruled out as the sole cause). 40 minutes gives real headroom above the observed ceiling without letting a genuinely hung test burn the 6-hour default.
