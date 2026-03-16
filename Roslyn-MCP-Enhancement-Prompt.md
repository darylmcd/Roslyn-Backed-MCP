# Roslyn MCP Server Enhancement Prompt

**Generated:** 2026-03-15
**Server Repo:** `C:\code-repo\roslyn-backed-mcp`
**Consumer Repo:** `C:\Code-Repo\DotNet-Network-Documentation`
**Motivation:** Gaps identified while planning execution of `Codebase-Refactoring-Prompt.md` — a 6-phase refactoring of a ~41,300 LOC .NET 10 codebase across 7 projects

---

## Context for the Implementing Agent

You are enhancing a custom Roslyn-backed MCP server. The server provides semantic C# analysis tools to AI coding agents in Cursor. It is a .NET 10 application using Roslyn workspaces, the ModelContextProtocol SDK (1.1.0), and Microsoft.CodeAnalysis 5.3.0.

### Server Architecture (read-only — do not restructure)

```
C:\code-repo\roslyn-backed-mcp\
├── RoslynMcp.slnx
├── src/
│   ├── Company.RoslynMcp.Host.Stdio/     # Entry point + MCP tool classes
│   │   ├── Program.cs                     # Generic host, stdio transport, WithToolsFromAssembly()
│   │   └── Tools/
│   │       ├── WorkspaceTools.cs          # workspace_load, workspace_reload, workspace_status, project_graph, source_generated_documents
│   │       ├── SymbolTools.cs             # symbol_search, symbol_info, go_to_definition, find_references, find_implementations, document_symbols, find_overrides, find_base_members, member_hierarchy, symbol_signature_help, symbol_relationships
│   │       ├── AnalysisTools.cs           # project_diagnostics, diagnostic_details, type_hierarchy, callers_callees, impact_analysis
│   │       ├── RefactoringTools.cs        # rename_preview/apply, organize_usings_preview/apply, format_document_preview/apply, code_fix_preview/apply
│   │       └── ValidationTools.cs         # build_workspace, build_project, test_discover, test_run, test_related
│   ├── Company.RoslynMcp.Core/            # DTOs, service interfaces, PreviewStore
│   │   ├── Services/
│   │   │   ├── IWorkspaceManager.cs
│   │   │   ├── ISymbolService.cs
│   │   │   ├── IDiagnosticService.cs
│   │   │   ├── IRefactoringService.cs
│   │   │   ├── IValidationService.cs
│   │   │   ├── IDotnetCommandRunner.cs
│   │   │   └── IPreviewStore.cs + PreviewStore.cs
│   │   └── Models/                        # ~25 DTO records (SymbolDto, LocationDto, ImpactAnalysisDto, etc.)
│   └── Company.RoslynMcp.Roslyn/          # Roslyn service implementations
│       ├── Services/
│       │   ├── WorkspaceManager.cs
│       │   ├── SymbolService.cs
│       │   ├── DiagnosticService.cs
│       │   ├── RefactoringService.cs
│       │   └── ValidationService.cs
│       └── Helpers/
│           ├── DotnetCommandRunner.cs
│           ├── DotnetOutputParser.cs
│           ├── SymbolMapper.cs
│           ├── SymbolResolver.cs
│           ├── SymbolHandleSerializer.cs
│           └── DiffGenerator.cs
└── tests/
    └── Company.RoslynMcp.Tests/
```

### Tool Registration Pattern

Tools are static classes with `[McpServerToolType]` containing static methods with `[McpServerTool(Name = "...")]`. Service interfaces are injected as method parameters. The host calls `WithToolsFromAssembly()` to discover them.

### Existing Capabilities (34 tools)

The server already provides strong coverage in these areas:
- **Workspace management:** load, reload, status, project graph, source-generated documents
- **Symbol discovery:** search, info, definition, document symbols, signature help
- **Relationship analysis:** references, implementations, overrides, base members, callers/callees, type hierarchy, member hierarchy, symbol relationships, impact analysis
- **Diagnostics:** project diagnostics with filtering, diagnostic details with curated fix options
- **Code transformations:** rename, format document, organize usings, code fix — all with preview/apply pattern
- **Validation:** build workspace/project, test discover, test run, test related

---

## What the Refactoring Needs and What's Missing

The consuming repo's refactoring prompt describes 6 phases. During analysis of how the Roslyn MCP would support each phase, the following gaps were identified. Each gap is a scenario where an agent would need to fall back to error-prone text search or manual inspection because the current MCP tools don't provide the semantic information needed.

---

## Enhancement 1: Property Accessor Query Tool

### The gap

**Phase 1 (Record Type Immutability)** requires converting ~200 mutable properties (`{ get; set; }`) across 20+ record classes to `{ get; init; }`. The agent needs to answer: "Which properties on `InterfaceRecord` have a `set` accessor?" and "Which call sites assign to those properties after construction?"

The current `symbol_search` and `symbol_info` tools return `SymbolDto` which includes `Modifiers` (a list of strings like `"public"`, `"static"`) but does **not** include property accessor information (get/set/init). The agent also can't distinguish between a reference that *reads* a property vs. one that *writes* to it.

`find_references` returns `LocationDto` (file, line, column) but doesn't classify whether each reference is a read, write, or definition — so the agent cannot filter for "only the sites that assign to `InterfaceRecord.Port`" without reading every referenced line of code and parsing it manually.

### Proposed tool: `find_property_writes`

Find all locations where a property is assigned to (written), excluding reads and the initial declaration/initializer.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| workspaceId | string | Yes | Workspace session identifier |
| filePath | string? | No | Source file containing the property |
| line | int? | No | 1-based line of the property |
| column | int? | No | 1-based column of the property |
| symbolHandle | string? | No | Symbol handle for the property |

**Returns:** A list of write-site locations, each annotated with:
- The location (file, line, column)
- Whether the write is in an object initializer (safe for `init`) or a post-construction assignment (breaks `init`)
- The containing method/type for context

### Supporting change: Enrich `SymbolDto` for properties

Add optional fields to `SymbolDto`:
- `HasGetter` (bool?)
- `HasSetter` (bool?)
- `SetterAccessibility` (string? — e.g. `"public"`, `"private"`, `"init"`)

This lets the agent query property accessor details from any tool that returns `SymbolDto` without a separate call.

### Implementation notes

- Roslyn's `IPropertySymbol` exposes `GetMethod`, `SetMethod`, and the set method's `IsInitOnly` property — these are the backing APIs
- Roslyn's `SymbolFinder.FindReferencesAsync` returns `ReferencedSymbol` which has `Locations` — each `ReferenceLocation` has `IsWrittenTo` — use this to classify read vs. write
- Object-initializer writes can be distinguished from post-construction assignments by checking if the write location is inside an `ObjectInitializerExpression` syntax node

### Affected files

- `ISymbolService.cs` — add `FindPropertyWritesAsync` method
- `SymbolService.cs` — implement using `SymbolFinder` + `IsWrittenTo` + syntax-node classification
- `SymbolTools.cs` — add `find_property_writes` MCP tool
- `SymbolDto.cs` — add `HasGetter`, `HasSetter`, `SetterAccessibility` fields
- `SymbolMapper.cs` — map `IPropertySymbol` accessor info to the new DTO fields

---

## Enhancement 2: Bulk Symbol Query Tool

### The gap

**Phase 1** needs to inspect all 20+ record types in a single file (`DeviceRecords.cs`, 392 lines) to understand their property accessor patterns. **Phase 3** needs to trace ~617 occurrences of `Dictionary<string, object?>` across 67 files to understand usage patterns. Currently, the agent must call `symbol_search` or `find_references` one symbol at a time. For 20+ types with 10-15 properties each, that's 200-300 sequential MCP calls.

### Proposed tool: `find_references_bulk`

Find references for multiple symbols in a single call.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| workspaceId | string | Yes | Workspace session identifier |
| symbols | array | Yes | Array of `SymbolLocator` objects (max 50) |
| includeDefinition | bool | No | Include the definition location in results (default: false) |

**Returns:** A map from each symbol handle/name to its list of reference locations. Symbols that can't be resolved are reported with an error message rather than failing the whole batch.

### Implementation notes

- This is primarily a performance/batching optimization — no new Roslyn APIs needed beyond what `find_references` already uses
- Process symbols in parallel using `Task.WhenAll` with a bounded concurrency (e.g. 4-8 concurrent resolution tasks) to avoid starving the Roslyn workspace
- Return results keyed by the input symbol's handle or name for easy correlation

### Affected files

- `ISymbolService.cs` — add `FindReferencesBulkAsync` method
- `SymbolService.cs` — implement with parallel resolution and aggregation
- `SymbolTools.cs` — add `find_references_bulk` MCP tool
- Consider a new `BulkReferenceResultDto.cs` for the return shape

---

## Enhancement 3: Type Usage Pattern Analysis

### The gap

**Phase 3 (Eliminate Dictionary Pipeline)** requires understanding *how* `Dictionary<string, object?>` is used at each of its 617 occurrences — is it a method return type, a parameter type, a local variable, a property type, a generic argument? The agent needs to classify usage patterns to plan the migration strategy (e.g., "all 45 occurrences in `CommandDispatcher` are method return types → change the return type" vs. "these 12 in `BuildSteps` are parameter types → change the parameter type").

The current `find_references` only returns locations. The agent would need to read every file and analyze the surrounding syntax manually to classify usage patterns.

### Proposed tool: `find_type_usages`

Find all usages of a type across the solution, classified by how the type is used.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| workspaceId | string | Yes | Workspace session identifier |
| filePath | string? | No | Source file containing the type |
| line | int? | No | 1-based line of the type |
| column | int? | No | 1-based column of the type |
| symbolHandle | string? | No | Symbol handle for the type |
| metadataName | string? | No | Fully qualified type name (e.g., `System.Collections.Generic.Dictionary<string, object?>`) |

**Returns:** Usages grouped by classification:
- `MethodReturnType` — methods that return this type
- `MethodParameter` — methods that accept this type as a parameter
- `PropertyType` — properties of this type
- `LocalVariable` — local variable declarations of this type
- `FieldType` — fields of this type
- `GenericArgument` — used as a generic type argument in another type
- `BaseType` — used as a base type or interface
- `Cast` — cast expressions or `as` expressions
- `TypeCheck` — `is` patterns
- `ObjectCreation` — `new` expressions creating this type
- `Other` — anything not classified above

Each usage includes: file, line, column, containing symbol, classification, and a short context snippet (the line of code).

### Implementation notes

- After resolving the type symbol, use `SymbolFinder.FindReferencesAsync` to get all reference locations
- For each reference location, walk up the syntax tree from the reference node to classify its role:
  - Parent is `ReturnTypeSyntax` → `MethodReturnType`
  - Parent is `ParameterSyntax` → `MethodParameter`
  - Parent is `PropertyDeclarationSyntax` type clause → `PropertyType`
  - Parent is `VariableDeclarationSyntax` → `LocalVariable` or `FieldType` depending on context
  - etc.
- For constructed generic types like `Dictionary<string, object?>`, the agent may pass the fully qualified constructed name via `metadataName` — the implementation should handle open and closed generic type matching

### Affected files

- `ISymbolService.cs` — add `FindTypeUsagesAsync` method
- `SymbolService.cs` — implement with reference finding + syntax classification
- `SymbolTools.cs` or `AnalysisTools.cs` — add `find_type_usages` MCP tool
- New DTO: `TypeUsageDto.cs` and `TypeUsageClassification` enum in `Models/`

---

## Enhancement 4: Classify Reference Reads vs. Writes

### The gap

This is a smaller, more general version of Enhancement 1 that benefits multiple phases. The current `find_references` tool returns raw `LocationDto` without indicating whether each reference is a read, write, definition, or other classification. This forces the agent to read surrounding code for *every* reference to understand usage intent.

The refactoring prompt has multiple phases where read/write classification matters:
- **Phase 1:** Identifying post-construction property assignments
- **Phase 3:** Understanding whether dict accesses are reads (`.GetStr()` calls) or writes (`.Add()`, `dict["key"] = value`)
- **Phase 5:** Understanding whether `ProjectedDeviceRow` fields are read by the sheet builders or mutated

### Proposed change: Enrich `find_references` output

Add a `Classification` field to reference locations returned by `find_references`. This is not a new tool — it enriches the existing one.

**New field on each reference location:**

| Field | Type | Description |
|-------|------|-------------|
| classification | string | One of: `Definition`, `Read`, `Write`, `ReadWrite`, `NameOf`, `Attribute`, `Other` |

### Implementation notes

- Roslyn's `ReferenceLocation` already exposes:
  - `IsDefinition`
  - `IsWrittenTo` (available via `FindReferencesAsync` with appropriate options)
- These properties directly map to the proposed classifications
- Compute `ReadWrite` when both `IsWrittenTo` is true and the expression is also read (e.g. `x += 1`)
- Check for `nameof()` and attribute usages via syntax node inspection

### Affected files

- `LocationDto.cs` — add optional `Classification` field (string?)
- `SymbolService.cs` — update `FindReferencesAsync` to pass `FindReferencesSearchOptions` that request write classification, and populate the new field
- `SymbolMapper.cs` — map `ReferenceLocation` flags to classification string

---

## Enhancement 5: Mutation Analysis for a Type

### The gap

**Phase 1** requires understanding whether `Device` objects are mutated after initial construction. The `Device` class has 30+ public settable properties and several mutator methods (`SetMgmtIp()`, `SetArea()`, etc.). The refactoring prompt asks: "Which of these are called during construction/building vs. after the device is 'complete'?"

The agent can use `callers_callees` on each mutator method individually, but there's no tool to ask the composite question: "Show me all mutation paths into this type — every method that modifies its state, and every call site that invokes those methods."

### Proposed tool: `find_type_mutations`

Given a type, find all methods/properties that mutate its state, and all external call sites that trigger those mutations.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| workspaceId | string | Yes | Workspace session identifier |
| filePath | string? | No | Source file containing the type |
| line | int? | No | 1-based line of the type |
| column | int? | No | 1-based column of the type |
| symbolHandle | string? | No | Symbol handle for the type |

**Returns:**
- List of **mutating members** on the type: methods with `set` property calls, methods that modify fields, methods that modify collection properties (`.Add()`, `.Clear()`, etc.)
- For each mutating member: list of **external callers** (file, line, containing method, containing type)
- A summary grouping callers by phase (constructor/init vs. post-construction) where possible — specifically, calls from within object initializers, constructors, or builder-pattern methods vs. calls from arbitrary consumer code

### Implementation notes

- Resolve the type symbol, enumerate all members
- For properties: check if `SetMethod` exists and is externally accessible
- For methods: use simple heuristic — methods that assign to `this.` fields/properties are mutating (walk the method body syntax tree for assignment expressions targeting instance members)
- For each mutating member, run `FindReferencesAsync` to find external callers
- Classify callers by context: is the caller inside a constructor, object initializer, or a "builder" method (method on the same type that returns `void`) vs. external consumer code
- This tool is inherently expensive — document it as a heavy analysis tool, not suitable for rapid iteration

### Affected files

- `ISymbolService.cs` or a new `IAnalysisService.cs` — add `FindTypeMutationsAsync`
- `SymbolService.cs` or new `AnalysisService.cs` — implement
- `AnalysisTools.cs` — add `find_type_mutations` MCP tool
- New DTOs: `TypeMutationDto.cs`, `MutatingMemberDto.cs` in `Models/`

---

## Enhancement 6: Filtered Test Execution

### The gap

The refactoring prompt specifies running targeted tests after each incremental change (e.g., "after migrating each parser, run its tests"). The current `test_run` tool accepts a `filter` parameter (dotnet test filter expression), but there's no tool to discover which tests are related to a *set* of changed files.

`test_related` takes a single symbol. When the agent changes 5 files in Phase 1, it would need to call `test_related` 5+ times (once per changed type), then deduplicate and construct a combined filter expression. A more natural workflow would be: "given these changed files, which tests should I run?"

### Proposed tool: `test_related_files`

Given a list of changed file paths, find all related tests.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| workspaceId | string | Yes | Workspace session identifier |
| filePaths | string[] | Yes | Array of absolute paths to changed source files |
| maxResults | int | No | Maximum number of test cases to return (default: 100) |

**Returns:** Deduplicated list of related test cases, each with:
- Test method name
- Test class name
- Test project
- File path and line
- Which of the input files triggered the relationship

### Implementation notes

- For each input file, call `GetDocumentSymbolsAsync` to find declared types, then `FindRelatedTestsAsync` for each type
- Deduplicate across files and sort by relevance
- Also include a combined `dotnet test --filter` expression the agent can pass directly to `test_run` — this saves the agent from constructing filter syntax manually

### Affected files

- `IValidationService.cs` — add `FindRelatedTestsForFilesAsync`
- `ValidationService.cs` — implement with per-file symbol discovery + test relation + dedup
- `ValidationTools.cs` — add `test_related_files` MCP tool

---

## Priority Order

| # | Enhancement | Impact on Refactoring | Complexity |
|---|------------|----------------------|------------|
| 4 | Classify reference reads vs. writes | HIGH — enriches existing tool, benefits Phases 1, 3, 5 | LOW — uses existing Roslyn API (`IsWrittenTo`) |
| 1 | Property accessor query / `SymbolDto` enrichment | HIGH — critical for Phase 1 (200 properties) | MEDIUM — new tool + DTO enrichment |
| 3 | Type usage pattern analysis | HIGH — critical for Phase 3 (617 occurrences) | MEDIUM — syntax tree classification |
| 6 | Filtered test execution by changed files | MEDIUM — quality-of-life for every phase | LOW — composition of existing functionality |
| 2 | Bulk symbol query | MEDIUM — performance for Phases 1, 3 | LOW — parallel wrapper around existing code |
| 5 | Mutation analysis for a type | MEDIUM — valuable for Phase 1 Device class | HIGH — composite analysis, expensive |

**Recommendation:** Implement enhancements 4, 1, and 3 first. These close the biggest gaps for Phases 1 and 3. Enhancement 6 is a nice-to-have that improves workflow for every phase. Enhancement 2 is a performance optimization. Enhancement 5 is the most complex and can be deferred — the agent can approximate mutation analysis by combining `callers_callees` with `find_property_writes` (Enhancement 1) after those are available.

---

## Session-Start Requirements for the Implementing Agent

Before making changes, read these files in the server repo (`C:\code-repo\roslyn-backed-mcp`):

1. `README.md` — project overview and conventions
2. `src/Company.RoslynMcp.Core/Services/ISymbolService.cs` — primary service interface to extend
3. `src/Company.RoslynMcp.Roslyn/Services/SymbolService.cs` — primary implementation to extend
4. `src/Company.RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` — tool registration pattern
5. `src/Company.RoslynMcp.Core/Models/SymbolDto.cs` — DTO to enrich
6. `src/Company.RoslynMcp.Core/Models/LocationDto.cs` — DTO to enrich
7. `src/Company.RoslynMcp.Roslyn/Helpers/SymbolMapper.cs` — mapping logic to extend
8. `src/Company.RoslynMcp.Roslyn/Helpers/SymbolResolver.cs` — symbol resolution pattern
9. `tests/Company.RoslynMcp.Tests/` — test patterns and conventions

## Constraints

- Preserve all 34 existing tools and their current behavior
- Follow the existing preview/apply pattern for any new tools that modify files
- New analysis-only tools do not need preview/apply — they are read-only
- Use the existing `SymbolLocator` pattern for symbol identification parameters
- Add tests for new tools using the existing test conventions and sample solutions
- Update tool JSON descriptors after adding new tools (or document how they are auto-generated)
- After changes, republish the server executable to `publish/` and verify it still loads in Cursor

## Verification

After implementation:
1. Build the solution: `dotnet build` with no errors or new warnings
2. Run the test suite: `dotnet test`
3. Load the consumer solution (`NetworkDocumentation.sln`) via `workspace_load` and verify the new tools work against real data:
   - `find_property_writes` on `InterfaceRecord.Port` should return write sites
   - `find_type_usages` on `Dictionary<string, object?>` should classify usages
   - `find_references` with classification on a well-known symbol should show read/write/definition
   - `test_related_files` for `DeviceRecords.cs` should find `DeviceRoundTripTests`
4. Republish: `dotnet publish` to update the executable in `publish/`
