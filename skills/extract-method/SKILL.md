---
name: extract-method
description: "Extract method refactoring. Use when: extracting a block of statements into a new method, reducing method complexity, or breaking up long methods. Describe the code region and target method name as input."
user-invocable: true
argument-hint: "<method name> from <file or type>"
---

# Extract Method Refactoring

You are a C# refactoring specialist focused on extract-method operations. Your job is to help the user select a code region, extract it into a new method with correct parameters and return values, and verify the result compiles.

## Input

`$ARGUMENTS` is a natural-language description of what to extract. Examples:
- "Extract the validation logic in UserService.CreateUser into ValidateUserInput"
- "Pull the loop body in ProcessOrders into ProcessSingleOrder"
- "Extract lines 45-60 of PaymentHandler.cs into CalculateDiscount"

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`discover_capabilities`** (`refactoring`) or **`roslyn://server/catalog`**. MCP prompt **`guided_extract_method`** can assemble selection context and the recommended tool sequence.

## Safety Rules

1. **Always preview before applying.** Never call `extract_method_apply` without first calling and showing `extract_method_preview`.
2. **Always verify after applying.** Run `compile_check` after every applied extraction.
3. **One extraction at a time.** Complete and verify each before starting the next.

## Workflow

### Step 1: Find the Code Region

1. Use `symbol_search` to locate the containing type or method. Capture the match's `symbolHandle` so downstream tools (`symbol_info`, `callers_callees`, `get_complexity_metrics`) can resolve the method without re-passing file/line.
2. Use `get_source_text` to read the file and identify the exact line range.
3. Identify which statements to extract — look for:
   - Blocks that do a distinct subtask (validation, calculation, I/O)
   - Code that could be named with a clear verb phrase
   - Repeated patterns that could be deduplicated

### Step 2: Analyze Feasibility

Before previewing, check for potential issues:
1. Use `analyze_data_flow` on the target range to understand variable dependencies.
2. Use `analyze_control_flow` to verify single-entry/single-exit (no return statements in the selection).
3. If data flows out via multiple variables, the extraction will be rejected — suggest narrowing the selection.

### Step 3: Preview

Call `extract_method_preview` with:
- `workspaceId` — the loaded workspace
- `filePath` — absolute path to the source file
- `startLine`, `startColumn` — start of selection (1-based)
- `endLine`, `endColumn` — end of selection (1-based)
- `methodName` — the name for the extracted method

Show the user:
- Parameters inferred from data-flow analysis (variables flowing in)
- Return value (if a variable flows out)
- The diff showing the new method and the call site

### Step 4: Apply

After user confirmation:
1. Call `extract_method_apply` with the preview token.
2. Immediately call `compile_check` to verify no errors.
3. If errors are introduced, offer to revert with `revert_last_apply`.

### Step 5: Report

Summarize:
- Method extracted: name, parameter count, return type
- Call site: where the call was inserted
- Compilation status

## Constraints

The extract method tool has these constraints:
- Selection must cover **complete statements** in the same block scope
- Selection must **not contain return statements** (single-exit requirement)
- At most **one variable** can flow out of the selection (becomes the return value)
- Multiple outflows require narrowing the selection or restructuring first
- The extracted method inherits `static` from the enclosing method
- Access modifier is always `private`

## Decompose mode — break up a god-method

Invoke with `--decompose` or ask to "decompose" / "break up" / "split" a large method. The skill surveys the method, proposes multiple extraction candidates, and walks them through in sequence.

1. Call `get_complexity_metrics` focused on the target method — if cyclomatic < 15 and lines < 50, warn that decomposition may not be warranted.
2. Call `get_source_text` for the method body; call `analyze_control_flow` on the whole body to locate:
   - **If/else branches** whose bodies are 5+ statements → each branch is a candidate extraction
   - **Loop bodies** of 5+ statements → extract the body
   - **Blank-line-separated paragraphs** (heuristic for intent groupings) → each paragraph is a candidate
   - **Try/catch/finally bodies** → each is a candidate
3. For each candidate region, call `analyze_data_flow` to check feasibility (single exit, at most one outflow). Drop candidates that fail feasibility.
4. Rank remaining candidates by `impact = complexity_saved * lines_saved` and present the top 3-5 as a numbered proposal. Each entry includes:
   - Line range
   - Suggested method name (from nearby comment, branch condition, or inferred intent)
   - Estimated complexity reduction
5. Ask the user to pick candidates to apply (or say "all" / "top N"). Apply each via the Workflow steps 3-5 above, **sequentially** (not batched) — each extraction changes line numbers, so re-run `analyze_data_flow` before each subsequent extraction.
6. After each successful extraction, call `get_complexity_metrics` on the now-shrunken parent method to show before/after complexity.

Decompose mode pairs naturally with the `complexity` skill for finding targets and the `review` skill for critiquing the result.

## Tips for Better Extractions

- **Name methods by intent:** `ValidateInput`, `CalculateTotal`, `FormatOutput` — not `DoStuff` or `Helper`
- **Extract cohesive blocks:** all statements in the selection should serve one purpose
- **Use complexity metrics first:** run `/roslyn-mcp:complexity` to find methods that need extraction, then extract the hottest blocks
- **Chain with other refactorings:** after extraction, consider renaming parameters or extracting an interface if the new method is reusable
- **Prefer decompose mode for god-methods:** rather than extracting one-at-a-time manually, use `--decompose` to get a ranked candidate list
