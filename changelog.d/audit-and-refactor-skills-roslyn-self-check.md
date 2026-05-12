---
category: Maintenance
---

- **Maintenance:** The `refactor`, `refactor-loop`, and `architecture-review` skills now append a session-end self-check block on exit: when the CWD is classified as a C# repo but zero Roslyn semantic tool calls were made, the skill emits `summary: { semanticCalls: 0, classificationApplied: "csharp" }` and a warning recommending a Roslyn-first rerun. Companion to `routine-flows-wrap-csharp-work-with-roslyn-bookends`.
