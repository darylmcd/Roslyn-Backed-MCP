# Prompt: Comprehensive test suite audit (quality, performance, design)

<!-- purpose: Reusable prompt to audit tests for slowness, duplication, workspace/init smells, and SRP—tests should find bugs, not add risk. -->

**Mission:** Audit every test for speed, focus, and design so the suite validates the product without becoming a source of slowness or instability—especially wherever tests pay repeated or poorly-scoped costs for **initialization**, **shared runtime state**, and **I/O-heavy dependencies** (workspace, filesystem, process, network).

---

## Full prompt (copy for agents)

Perform a **full pass over the automated tests** in this repository. The goal is to ensure the test code **surfaces product defects** without **creating its own** reliability or performance problems.

### Performance and efficiency

Prioritize **categories of risk**, then tie findings to **concrete code paths** (file, type, method).

- Flag patterns that tend to make tests **slow, flaky, or resource-heavy**, including:
  - **Lifecycle mistakes:** one expensive resource created per test when suite- or class-scoped reuse (with correct isolation) would do; missing teardown; leaks or ordering dependence.
  - **Duplicate work:** repeated solution/workspace/project loads, repeated DI container builds, redundant builds or file I/O where a narrower or cached setup suffices.
  - **Contention and ordering:** shared locks, static mutable state, or global gates exercised in parallel without clear rules.
  - **Time-based hacks:** `Thread.Sleep`, arbitrary timeouts, or polling used in place of deterministic synchronization or proper await boundaries.
  - **Over-broad tests:** integration surface larger than the behavior under test, when a slimmer test would catch the same defect faster.

**Illustrative hotspots** in this codebase have included paths involving **service initialization** (e.g. patterns around `InitializeServices` and test bootstrapping), **`WorkspaceManager`**, and **async workspace load** (e.g. `LoadAsync` and similar). Treat these names as **examples of expensive lifecycle and I/O boundaries**, not as an exhaustive checklist. **Report any code with comparable cost or coupling**, including renamed APIs, new helpers, or different entry points that play the same role.

**Anti-anchoring:** Do **not** stop after searching for those identifiers. Severity and recommendations should reflect **whether the pattern is inherently expensive or poorly scoped**, not whether it matches a historical name.

### Design and architecture

- Assess **single responsibility** within test classes and helpers: helpers that do too much, “god” fixtures, and tests that assert multiple unrelated behaviors.
- Call out **architectural smells in tests**: unclear layering (fast unit-style vs heavier integration), tests that depend on implementation details of unrelated modules, and shared mutable state without clear lifecycle rules.
- Prefer **framework-agnostic language** when describing setup: e.g. “suite-level or class-level one-time setup” rather than assuming only one test framework’s attributes—use the patterns this repo actually uses (MSTest, xUnit, NUnit, etc.) when you cite files.

### Bar for “good” tests

- Prefer **minimal, focused** tests: one clear intent per test, the smallest realistic surface area, and fast feedback.
- Recommend **concrete refactors** where they reduce duplication and risk without hiding real integration gaps: shared fixtures where isolation allows, **deferring** heavy work until a test actually needs it, substituting **fakes or narrow doubles** for I/O-heavy dependencies when the test does not require the full stack.

### Deliverable

- A structured report: **issues found** (with file/class references), **severity** (e.g. slow, fragile, SRP, duplication), and **actionable recommendations**. Be thorough; prioritize clarity over brevity where tradeoffs matter.
- Where an issue resembles a known pattern (e.g. repeated workspace load) but uses **different** symbols, say so explicitly so maintainers see the **category**, not only a string match.
