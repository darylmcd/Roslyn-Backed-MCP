---
name: snippet-eval
description: "Quick C# feedback without loading a solution. Use when: validating syntax, semantics, or running a small script; prototyping an expression; or explaining an isolated code fragment."
user-invocable: true
argument-hint: "snippet path | inline code | script"
---

# Snippet and Script Evaluation

You use **`analyze_snippet`** and **`evaluate_csharp`** for fast feedback when loading a full **`.sln`** is unnecessary or impossible.

## Input

`$ARGUMENTS` should indicate either:

- A **short C# fragment** to analyze or run, or
- A **file path** to read and treat as snippet/script (if the user points at a scratch file)

## Server discovery

Full solution workflows live in **`roslyn://server/catalog`**. This skill is for **ephemeral** analysis only.

## When to use which tool

| Goal | Tool |
|------|------|
| Parse, bind, diagnostics on a fragment | **`analyze_snippet`** |
| Run a script / evaluate an expression (side effects, output) | **`evaluate_csharp`** |
| Production refactor / cross-file impact | Load workspace with **`workspace_load`** and use symbol tools |

## Workflow

1. For **compile/bind** questions: call **`analyze_snippet`** with the code and appropriate options (as exposed by the tool schema).
2. For **execution**: call **`evaluate_csharp`**; warn the user that scripts run in the host process trust boundary.
3. If results imply real-project dependencies, recommend **`workspace_load`** on the actual solution.

## Explain mode — semantic walk-through for teaching or learning

Invoke with `--explain` or ask "explain this snippet" / "what does this do?". The skill produces a step-by-step semantic walkthrough instead of a pass/fail result:

1. Call **`analyze_snippet`** to bind the snippet and gather diagnostics.
2. Call **`get_operations`** (if available) on the snippet to walk the IOperation tree — this yields the full semantic tree: local declarations, invocations, branches, loops, lambdas, await points.
3. For each top-level construct (declaration, expression, statement):
   - Name the operation kind (e.g., `LocalReference`, `InvocationOperation`, `ConditionalAccessOperation`)
   - State the inferred **type** and (for numeric/string literals) the **value**
   - Explain data-flow implications: what variables are read, what gets assigned, what captures (for lambdas/closures)
4. If the snippet uses `await`, show the async flow: where the state machine suspends, what continuation happens on completion vs exception.
5. If the snippet uses LINQ, explain deferred vs immediate execution and what the query translates to.
6. Conclude with a single-sentence summary: "This snippet X, producing Y, with no/these side effects."

Explain mode is read-only — it never calls `evaluate_csharp`. It's useful for teaching newcomers, debugging why a snippet doesn't compile, or understanding inherited code.

## Safety

Treat **`evaluate_csharp`** as **arbitrary code execution** in the MCP host environment. Only run code the user explicitly supplied and avoid secrets in snippets. Explain mode is safe — it never executes.
