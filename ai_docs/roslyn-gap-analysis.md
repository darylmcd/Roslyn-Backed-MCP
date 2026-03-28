# Roslyn SDK Gap Analysis vs. MCP Server v1.1.0

> Generated: 2026-03-28 | Server: v1.1.0 (Roslyn 5.3.0.0, .NET 10.0.5)
> 104 tools currently implemented (49 stable + 55 experimental)

---

## Currently Implemented (Summary)

| Category | Count | Examples |
|---|---|---|
| Workspace Management | 6 | load, close, reload, status, list, server_info |
| Navigation & Symbols | 9 | go_to_definition, symbol_info, symbol_search, semantic_search |
| References & Calls | 7 | find_references, find_implementations, callers_callees |
| Diagnostics & Analysis | 5 | project_diagnostics, security_diagnostics, get_completions |
| Code Actions & Fixes | 5 | get_code_actions, preview/apply_code_action, code_fix_preview/apply |
| Rename & Format | 4 | rename_preview/apply, format_document_preview/apply |
| Organize Usings | 2 | organize_usings_preview/apply |
| Interface Extraction | 5 | extract_interface (same-project, cross-project, DI wiring) |
| Type Extraction & Split | 5 | extract_type, split_class, move_type_to_file |
| Type/File Moving | 4 | move_type_to_project, move_file |
| Bulk Refactoring | 2 | bulk_replace_type_preview/apply |
| Dead Code | 4 | find_unused_symbols, remove_dead_code, find_type_usages |
| Code Metrics | 4 | complexity_metrics, cohesion_metrics, find_shared_members, find_type_mutations |
| Consumer & Dependency | 5 | find_consumers, find_property_writes, namespace_deps, nuget_deps, DI registrations |
| Syntax & Source | 2 | get_syntax_tree, get_source_text |
| Editing & Files | 8 | apply_text_edit, multi_file_edit, create/delete_file, impact_analysis |
| Project Mutation | 11 | add/remove package/project refs, target frameworks, properties, migrate_package |
| Build & Test | 7 | build_workspace/project, test_run/discover/related/coverage |
| Scaffolding | 4 | scaffold_type, scaffold_test |
| Project Graph | 2 | project_graph, source_generated_documents |
| Undo | 1 | revert_last_apply |

---

## Roslyn Capabilities NOT Exposed (43 Gaps)

### A. Compiler / Emit APIs

| # | Capability | Description |
|---|---|---|
| 1 | **In-Memory Compilation (Compilation.Emit)** | Compile C# to IL/assemblies in-memory without shelling out to `dotnet build`. Enables fast compilability checks, snippet validation, and assembly production without MSBuild overhead. |
| 2 | **Pure Compiler Diagnostics (Compilation.GetDiagnostics)** | Get compiler diagnostics without a full MSBuild build. Faster and lighter when only type-checking is needed. |
| 3 | **Incremental Compilation / What-If Analysis** | Replace a syntax tree in a Compilation and check if it still compiles. Enables "would this change break anything?" without a rebuild. |

### B. Scripting APIs

| # | Capability | Description |
|---|---|---|
| 4 | **C# Script Evaluation (CSharpScript.EvaluateAsync)** | REPL-style evaluation of C# expressions/scripts at runtime. Enables interactive prototyping, expression testing, and dynamic code execution. |
| 5 | **Script Compilation & Delegate Creation** | Pre-compile scripts for repeated execution. Enables user-defined analysis rules or custom refactoring logic expressed as C#. |

### C. Data Flow & Control Flow Analysis

| # | Capability | Description |
|---|---|---|
| 6 | **DataFlowAnalysis (SemanticModel.AnalyzeDataFlow)** | Analyze variable flow through a code region: reads, writes, captures, always-assigned. Enables deep lifetime analysis, side-effect detection, and dependency understanding. |
| 7 | **ControlFlowAnalysis (SemanticModel.AnalyzeControlFlow)** | Analyze entry/exit points, reachability, and branching within code regions. Enables unreachable-code detection and path validation. |

### D. Classification

| # | Capability | Description |
|---|---|---|
| 8 | **Semantic Classification (Classifier.GetClassifiedSpans)** | Semantically-aware token classification (distinguishing type names from variables, keywords from identifiers). Richer than syntax-only highlighting. |

### E. Syntax Transformation APIs

| # | Capability | Description |
|---|---|---|
| 9 | **Programmatic Syntax Rewriting (CSharpSyntaxRewriter)** | Pattern-based AST rewriting. Enables custom automated transformations (e.g., convert `var` to explicit types, apply naming conventions systematically). |
| 10 | **SyntaxGenerator (Microsoft.CodeAnalysis.Editing)** | Language-agnostic code generation API. Enables generating boilerplate (DTOs, builders, mappers) from specifications without string templating. |
| 11 | **SyntaxFactory Direct Access** | Fine-grained syntax node construction with precise control over trivia and whitespace. |

### F. Semantic Model Deep Queries

| # | Capability | Description |
|---|---|---|
| 12 | **Expression-Level Type Queries** | Query inferred type of any expression, resolved overload, or implicit conversion -- not just at declaration positions. |
| 13 | **GetConstantValue** | Extract compile-time constant values. Enables constant propagation analysis and magic number detection. |
| 14 | **Alias & Preprocessor Resolution** | Resolve `using` aliases and `#if` preprocessor symbols. Enables understanding conditional compilation. |
| 15 | **LookupSymbols / LookupNamespacesAndTypes** | Query all symbols visible at a scope position. More complete than completions for full scope analysis. |

### G. Operations API

| # | Capability | Description |
|---|---|---|
| 16 | **IOperation Tree** | Language-agnostic intermediate representation of code behavior (assignments, invocations, loops). Enables behavioral pattern matching at a higher abstraction than syntax trees. |

### H. Analyzer Infrastructure

| # | Capability | Description |
|---|---|---|
| 17 | **Dynamic Analyzer Loading** | Load and run third-party Roslyn analyzers (StyleCop, SonarAnalyzer, custom) against the workspace dynamically. |
| 18 | **Suppression & Severity Management** | Programmatic management of pragma suppressions, SuppressMessage attributes, and severity overrides (.editorconfig / rulesets). |
| 19 | **Analyzer Performance Profiling** | Expose analyzer execution time data to identify slow analyzers degrading builds. |

### I. Source Generators (Extended)

| # | Capability | Description |
|---|---|---|
| 20 | **Generator Debugging / Inspection** | Inspect registered generators, their execution diagnostics, and test generators in isolation. |
| 21 | **Incremental Generator Pipeline Inspection** | Inspect pipeline stages (init, source-provided, post-init) to understand why a generator fires or doesn't. |

### J. Workspace / Solution Manipulation (Extended)

| # | Capability | Description |
|---|---|---|
| 22 | **Solution-Level Transformations** | Create new projects within a solution, duplicate projects, or perform arbitrary solution-level mutations beyond the current project mutation tools. |
| 23 | **AdhocWorkspace / Snippet Analysis** | Create ephemeral workspaces for analyzing code snippets without loading a full solution. Enables diff review, paste analysis, and snippet validation. |
| 24 | **Workspace Change Events / Streaming** | Expose real-time workspace change notifications (file added/removed/changed) as a streaming tool for reactive workflows. |

### K. Code Fixes & Refactoring (Extended)

| # | Capability | Description |
|---|---|---|
| 25 | **List All Analyzers & Rules** | Enumerate all loaded analyzers and their diagnostic IDs. Enables understanding analysis coverage. |
| 26 | **Batch Code Fix (FixAllProvider)** | Apply a code fix to ALL instances of a diagnostic across the solution in one operation (e.g., remove all unused usings everywhere). |
| 27 | **Extract Method** | Built-in Roslyn refactoring to extract selected code into a new method. A fundamental refactoring not individually exposed. |
| 28 | **Extract Local Variable / Introduce Variable** | Extract a subexpression into a named local variable. |
| 29 | **Inline Method / Inline Variable** | Replace a method call or variable with its body/value inline. |
| 30 | **Encapsulate Field** | Convert a public field to a property with getter/setter. |

### L. Formatting & Style

| # | Capability | Description |
|---|---|---|
| 31 | **Range Formatting** | Format only a selected range within a document instead of the entire file. More efficient and less disruptive. |
| 32 | **Formatting Options Query/Set** | Get/set formatting rules (indentation, brace style, spacing) programmatically. |
| 33 | **.editorconfig Read/Write** | Programmatic access to .editorconfig settings and their effect on the workspace. Query active style rules and modify them. |

### M. LSP Features Not Individually Exposed

| # | Capability | Description |
|---|---|---|
| 34 | **Document Highlights (read/write classification)** | Highlight all occurrences in a document with read vs. write distinction. Partially covered by find_references but without in-file read/write classification. |
| 35 | **Selection Range (smart expand/shrink)** | Semantic selection expansion: expression -> statement -> block -> method -> class. |
| 36 | **Folding Ranges** | Collapsible code regions (imports, methods, classes, #region). |
| 37 | **Inlay Hints** | Inline type annotations and parameter names at call sites for readability. |
| 38 | **Document Links** | Extract navigable URLs and file references from comments and strings. |

### N. Code Metrics (Extended)

| # | Capability | Description |
|---|---|---|
| 39 | **Maintainability Index** | Composite metric of cyclomatic complexity, LOC, and Halstead volume. Standard quality dashboard metric. |
| 40 | **Class Coupling** | Count unique types referenced by a class. Identifies overly coupled types. |

### O. MSBuild Integration (Extended)

| # | Capability | Description |
|---|---|---|
| 41 | **MSBuild Property/Item Evaluation** | Query resolved MSBuild properties and items (intermediate paths, resolved assets, conditional values). |
| 42 | **Build Target Listing & Execution** | List and invoke specific MSBuild targets (Publish, Pack, Clean, custom targets) beyond Build/Test. |
| 43 | **Project SDK Detection & Metadata** | Query SDK type, implicit imports, and global usings for a project. |

---

## Top 10 Recommended for Next Implementation

Ranked by: (1) unique value Roslyn provides over shell tools, (2) agent workflow impact, (3) implementation feasibility, (4) user demand signal.

### 1. Batch Code Fix -- FixAllProvider (Gap #26)
**Priority: CRITICAL**
- **Why:** This is the single highest-leverage missing capability. Agents frequently need to clean up entire solutions (remove unused usings, apply nullable annotations, fix naming conventions). Today each diagnostic must be fixed one at a time. A "fix all in solution" tool would turn an O(n) workflow into O(1).
- **Roslyn API:** `FixAllProvider`, `FixAllContext`, `BatchFixAllProvider`
- **Effort:** Medium -- requires wiring into the existing code-fix infrastructure with scope options (document / project / solution).

### 2. DataFlowAnalysis (Gap #6)
**Priority: HIGH**
- **Why:** Understanding how data flows through code is essential for safe refactoring. Agents need to know which variables are read, written, captured by closures, or always assigned before they can safely extract methods, inline variables, or move code. No shell tool provides this -- it's pure Roslyn semantic model capability.
- **Roslyn API:** `SemanticModel.AnalyzeDataFlow(statementRange)`
- **Effort:** Medium -- the API is straightforward; the tool design (input region specification) needs care.

### 3. ControlFlowAnalysis (Gap #7)
**Priority: HIGH**
- **Why:** Complements DataFlowAnalysis. Detecting unreachable code, validating all paths return, and understanding branching is critical for code quality analysis. Pairs naturally with complexity_metrics.
- **Roslyn API:** `SemanticModel.AnalyzeControlFlow(statementRange)`
- **Effort:** Low -- very similar shape to DataFlowAnalysis; can ship as a pair.

### 4. C# Script Evaluation (Gap #4)
**Priority: HIGH**
- **Why:** Enables agents to test expressions, prototype logic, and validate code interactively without creating files or running full builds. Unique capability that transforms how agents can interact with C# -- no other tool provides this.
- **Roslyn API:** `Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.EvaluateAsync`
- **Effort:** Medium -- needs sandboxing and security considerations, but the API is clean.

### 5. AdhocWorkspace / Snippet Analysis (Gap #23)
**Priority: HIGH**
- **Why:** Agents often need to analyze code fragments (from chat, diffs, or clipboard) without loading a full solution. An ephemeral workspace for quick type-checking, symbol resolution, or syntax validation of snippets would be enormously useful.
- **Roslyn API:** `AdhocWorkspace`, `Document.WithText()`, `Compilation.GetDiagnostics()`
- **Effort:** Medium -- standalone workspace management is well-documented.

### 6. Range Formatting (Gap #31)
**Priority: MEDIUM-HIGH**
- **Why:** After edits or code generation, agents typically need to format only the changed region, not the entire file. Full-document formatting creates noisy diffs. Range formatting is a standard LSP feature that's missing.
- **Roslyn API:** `Formatter.FormatAsync(document, spanToFormat)`
- **Effort:** Low -- small extension of the existing format_document tool.

### 7. .editorconfig Read/Write (Gap #33)
**Priority: MEDIUM-HIGH**
- **Why:** .editorconfig drives formatting, naming conventions, and analyzer severity. Agents need to understand the active style rules and sometimes modify them (e.g., enable nullable, change indentation). Currently opaque.
- **Roslyn API:** `AnalyzerConfigDocument`, `AnalyzerConfigOptions`
- **Effort:** Medium -- needs file parsing and option resolution chain understanding.

### 8. In-Memory Compilation (Gap #1)
**Priority: MEDIUM-HIGH**
- **Why:** The current `build_workspace`/`build_project` tools shell out to `dotnet build`, which is slow and heavyweight. Roslyn's `Compilation.Emit` can validate compilability in-memory in milliseconds. Perfect for rapid feedback loops during code generation.
- **Roslyn API:** `Compilation.Emit(Stream)`, `Compilation.GetDiagnostics()`
- **Effort:** Medium -- the API is simple but needs careful integration with the MSBuildWorkspace snapshot.

### 9. IOperation Tree (Gap #16)
**Priority: MEDIUM**
- **Why:** IOperation provides a language-agnostic, normalized view of code behavior. Enables writing analysis tools that reason about what code *does* rather than how it's *written*. Valuable for custom pattern detection (e.g., "find all methods that catch and swallow exceptions" or "find SQL string concatenation").
- **Roslyn API:** `SemanticModel.GetOperation(syntaxNode)`
- **Effort:** Medium -- the tree is rich; the challenge is useful serialization and query design.

### 10. List All Analyzers & Rules (Gap #25)
**Priority: MEDIUM**
- **Why:** Before fixing diagnostics or managing suppressions, agents need to know what analyzers are loaded and what rules they provide. This is a discovery/transparency tool that makes the existing diagnostic tools more useful.
- **Roslyn API:** `Compilation.Analyzers`, `DiagnosticAnalyzer.SupportedDiagnostics`
- **Effort:** Low -- enumeration of already-loaded analyzer state.

---

## Honorable Mentions (Next 5)

| Rank | Gap | Rationale |
|---|---|---|
| 11 | Custom Syntax Rewriter (#9) | Powerful but hard to expose safely through a tool interface |
| 12 | Suppression Management (#18) | Natural follow-on to "List Analyzers" |
| 13 | Maintainability Index (#39) | Natural extension of existing metrics tools |
| 14 | Dynamic Analyzer Loading (#17) | High value but complex security/isolation story |
| 15 | Incremental Compilation (#3) | Powerful but overlaps with in-memory compilation |
