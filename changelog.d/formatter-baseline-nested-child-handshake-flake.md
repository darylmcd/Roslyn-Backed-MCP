---
category: Maintenance
---

- **Maintenance:** Removed two load-sensitive test flakes: the formatter-baseline timed-out-capture test now starts its 2-second generator timeout only after its nested PowerShell fixture publishes readiness (previously two cold starts raced the timeout), and Windows directory-junction fixtures are created in-process via `FSCTL_SET_REPARSE_POINT` instead of a `cmd /c mklink /J` launch whose 5-second wait silently turned link-boundary tests inconclusive under load.
