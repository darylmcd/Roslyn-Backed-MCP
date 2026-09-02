---
category: Fixed
---

- **Fixed:** the release gate's changelog parsing no longer depends on the working tree's line endings. `verify-breaking-version-bump.ps1` anchored both its release-header and `Changed — BREAKING` patterns on `[ \t]*$`, which cannot match a line ending `\r\n`, so on a CRLF checkout every release header vanished (measured: 79 matches on LF, 0 on CRLF) and the gate aborted claiming there was no release section — while the sibling BREAKING pattern failed open, letting a breaking release pass a patch bump. Both now tolerate an optional carriage return, and a zero-match result distinguishes "no release section" from "headers present but off-contract" instead of reporting both as missing. Closes `release-header-regex-crlf-intolerant`.
