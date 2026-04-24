---
category: Fixed
---

- **Fixed:** `find_dead_fields` now classifies `acc.ErrorCount++`, `acc.ErrorCount += 2`, and `Interlocked.Increment(ref acc.ErrorCount)` as writes (previously mis-classified as reads because the reference node resolved to the `Name` of a `MemberAccessExpression` that only matched when the whole member-access was the LHS of a simple assignment). `UnusedCodeAnalyzer.ClassifyDeadFieldReferenceLocation` now unwraps member-access names and treats every non-`=` compound operator (`+=`, `-=`, `*=`, `/=`, `%=`, `|=`, `&=`, `^=`, `<<=`, `>>=`, `>>>=`, `??=`) as ReadWrite (`find-dead-fields-compound-assignment-writes`).
