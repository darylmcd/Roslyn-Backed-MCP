---
category: Fixed
---

- **Fixed:** Raised the MSTest timeout on the non-cooperative-script evaluation test so the harness no longer kills it before its own termination assertion can run, which had turned a load-tolerant assertion into a hard 10-second failure under CI contention.
