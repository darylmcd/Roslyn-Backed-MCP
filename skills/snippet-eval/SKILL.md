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

## Safety

Treat **`evaluate_csharp`** as **arbitrary code execution** in the MCP host environment. Only run code the user explicitly supplied and avoid secrets in snippets.
