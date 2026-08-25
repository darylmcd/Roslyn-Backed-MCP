---
name: semantic-find
installed_as: roslyn-mcp:semantic-find
description: "Natural-language code search across a C# codebase. Use when: looking for a class, method, or pattern described in plain English (e.g. 'the class that handles payment refunds', 'methods that retry on failure'), locating code by behavior rather than exact name, or orienting in an unfamiliar solution. Takes a natural-language description as input."
user-invocable: true
argument-hint: "natural-language description of the code you want to find"
---

# Semantic Code Search

You are a C# code-search specialist. Your job is to translate the user's natural-language description into the right Roslyn MCP lookup, surface ranked candidate locations, and optionally drill into the top hits with enough surrounding context for the user to recognize the code they wanted.

## Input

`$ARGUMENTS` is a natural-language description of the code the user is hunting for. It may describe **behavior** ("the class that handles payment refunds"), a **pattern** ("methods that retry on failure"), a **role** ("the entry point for webhook dispatch"), or lean toward an **identifier** ("anything named `RefundProcessor`"). Examples:

- "the class that handles payment refunds"
- "methods that retry on failure with exponential backoff"
- "where we validate JWTs before hitting the DB"
- "code that reads from the Stripe webhook queue"
- "RefundProcessor" (name-like — falls back to `symbol_search`)

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`search` or `all`) for the live tool list and **WorkflowHints** around semantic vs symbolic search.

## Connectivity precheck

Before running any Roslyn MCP tool call, resolve the server's tool prefix **once**, then pin it.

> **The prefix is client-assigned — never hard-code it.** Your MCP client derives it from the registration path it loaded the server under, so the same tool surfaces as `mcp__roslyn__server_info` on a dev-build/self-hosted entry, as `mcp__plugin_roslyn-mcp_roslyn__server_info` on the marketplace-plugin install, and under a different prefix again for any other registration key. Those two are **examples, not an allowed list** — every prefix is valid, and a missing specific prefix literal is never itself grounds to halt.

1. **Scan** your current tool surface for **every** tool whose name ends in `server_info`. `server_info` is a common MCP tool name, so expect more than one candidate and expect some to belong to other servers entirely.
2. **Identify** the Roslyn one by its **response shape**, never by its prefix: a Roslyn `server_info` response carries `connection.state`, `catalogVersion`, and `surface.*`. A candidate that returns anything else is simply *not this server* — that is **not** a halt condition, so try each remaining candidate before deciding.
3. **Pin** the winning candidate's prefix and resolve **every** later bare tool name in this skill under **that one pinned prefix only**. Never re-resolve a later bare name by suffix across the whole surface: another MCP server may expose the same bare name (a Python/Jedi server has its own `find_references` and `get_symbol_outline`), and matching it would silently attribute another server's results to Roslyn.
4. Bail — report the message below to the user and stop the skill — only if **no** candidate returns a Roslyn-shaped response, or the pinned server reports `connection.state` as anything other than `"ready"` (`initializing`, `degraded`, absent):

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server: some tool whose name **ends in** `server_info` must be callable, return a Roslyn-shaped response (`connection.state`, `catalogVersion`, `surface.*`), and report `connection.state: "ready"`. The prefix does not matter — your client assigns it from the registration path, so it may be `mcp__roslyn__…`, `mcp__plugin_roslyn-mcp_roslyn__…`, or anything else. Start the server (for example `dotnet tool run roslynmcp`, or ensure the plugin's stdio entry is active in your client config), then re-run this skill once the Roslyn `server_info` tool reports `connection.state: "ready"` under whichever prefix your tool surface exposes. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

5. Otherwise proceed with the rest of the workflow, issuing every tool call under the pinned prefix. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for search.

### Step 1: Load Workspace (if needed)

1. Call `workspace_list` to see if a workspace is already loaded.
2. If none is loaded, ask the user for a `.sln`, `.slnx`, or `.csproj` path and call `workspace_load`.
3. Call `workspace_status` to confirm readiness and note any load-time warnings.

### Step 2: Classify the Query

Decide whether the query is **semantic** (behavior, role, intent) or **symbolic** (looks like an identifier).

- **Semantic signals**: verbs, prose, "the thing that does X", question-like phrasing, multi-word descriptions without an obvious PascalCase token.
- **Symbolic signals**: PascalCase / camelCase tokens, single-word names, quoted identifiers, explicit mentions of "class named X" or "method called Y".

When in doubt, default to semantic-first and fall back to symbolic if semantic returns nothing useful.

### Step 3: Run the Primary Search

**Semantic queries** → call `semantic_search` with the user's description. Request a reasonable top-K (e.g. 10-20 hits) so ranking has room to discriminate.

**Symbolic queries** → call `symbol_search` with the name fragment. If results are sparse or off-target, also issue a `semantic_search` with the same token treated as prose.

If **neither** produces confident hits, widen the query (strip qualifiers, try synonyms, split into sub-phrases) and re-run. Do not loop more than 2-3 times — surface what you have and let the user refine.

### Step 4: Rank and Present

From the combined results:

1. Deduplicate by symbol (keep the highest-scoring hit per declaration).
2. Sort by score descending; break ties by accessibility (public first) and then by file path.
3. Keep the top 5-10 for the response. Note how many more were discarded if the result set was large.

### Step 5: Drill Into Top Hits (optional)

For the top 1-3 results — or any the user explicitly picks — enrich with:

1. `symbol_info` for kind, containing type, accessibility, signature, and XML docs.
2. `get_source_text` for a short surrounding snippet (the declaration plus a few lines of body).
3. `document_symbols` on the hit file if the user wants to see siblings at a glance.
4. `enclosing_symbol` if a raw position came back and you need the containing method/class for context.

Keep the enrichment proportional to the query — a vague question gets light enrichment; a "show me the method" request gets the full snippet.

### Step 6: Offer Next Actions

After presenting results, suggest follow-ups that match the likely intent:

- Rename or reshape the hit → skill **`refactor`**
- See callers / callees → `find_references`, `callers_callees`
- Pull a block out → skill **`extract-method`**
- Apply a quick fix at the hit → skill **`code-actions`**
- Deeper inspection of the containing type → skill **`analyze`** (at project scope) or `type_hierarchy`

Pick 1-2 that actually fit; don't dump the full menu.

## Query Guidance

`semantic_search` ranks over symbol names, XML doc comments, and surrounding source text. It is best at:

- **Behavior / role** phrasing ("handles payment refunds", "retries on failure")
- **Domain vocabulary** that appears in identifiers or doc comments ("webhook dispatch", "JWT validation")
- **Short descriptive phrases** (3-8 words of signal, not a paragraph)

It is **weaker** at:

- **Cross-cutting concerns** invisible in source (e.g. "the slow code path") — prefer `get_complexity_metrics`
- **Pure structural queries** ("all classes that implement IDisposable") — prefer `find_implementations` or `type_hierarchy`
- **Exact-name lookups** — prefer `symbol_search`
- **Questions about diagnostics** — prefer skill **`explain-error`** or `project_diagnostics`

### Good vs Poor Queries

| Good | Why |
|------|-----|
| "the class that handles payment refunds" | Behavior + domain term |
| "methods that retry on failure" | Pattern keyword likely in doc / code |
| "where we validate JWTs before hitting the DB" | Concrete role + domain vocab |
| "Stripe webhook queue reader" | Specific domain nouns |

| Poor | Why | Better |
|------|-----|--------|
| "refactor this" | No search signal at all | Describe what to find first |
| "the broken code" | Not in identifiers or docs | Use `project_diagnostics` |
| "fast methods" | Runtime behavior, not text | Use `get_complexity_metrics` |
| "`RefundProcessor`" | Exact identifier | Use `symbol_search` directly |

## Output Format

Present a ranked list. Each row: **file:line** — **symbol name** — score — short snippet.

```
## Semantic Find: "{query}"

### Top Results

1. src/Payments/RefundProcessor.cs:42 — `RefundProcessor.ProcessAsync` — 0.91
   public async Task<RefundResult> ProcessAsync(RefundRequest req)
   {
       // Issues refund via Stripe, writes audit row, emits event.

2. src/Payments/RefundService.cs:18 — `RefundService` — 0.84
   public sealed class RefundService : IRefundService
   {
       // Orchestrates refund workflow across gateway and ledger.

3. src/Payments/Handlers/RefundWebhookHandler.cs:23 — `RefundWebhookHandler.Handle` — 0.77
   ...

(showing 3 of 12 hits; ask to see more)

### Suggested Next Actions
- Rename `RefundProcessor.ProcessAsync` → skill `refactor`
- Find callers of `RefundService` → `find_references`
```

If the top score is low (e.g. < 0.5) or results look off-target, say so plainly and suggest a reworded query rather than pretending the match is strong.

## Refusal conditions

Refuse and exit cleanly when:

- **Empty or whitespace-only query** — ask the user to describe what they are looking for, with 1-2 example phrasings.
- **No workspace loaded and user declines to provide a path** — explain the skill needs a loaded solution/project to search against and stop.
- **Connectivity precheck failed** — emit the precheck message and stop (do not attempt searches against an unready server).
