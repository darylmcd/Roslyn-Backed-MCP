# Sampling-driven tool flows — spike note
<!-- purpose: Spike results for sampling-driven MCP tool flows. -->

**Initiative:** `sampling-driven-tool-flows-spike`
**Date:** 2026-05-05
**Author:** spike subagent
**Bound:** half-day, no production code beyond this note + a `changelog.d/` fragment.
**Outcome:** see [Verdicts](#verdicts) — one `go`, two `no-go`. Per-candidate follow-up rows filed for the `go` verdict only.

---

## (a) Call-shape audit — `McpServerExtensions.SampleAsync`

The C# SDK exposes server-initiated sampling on `ModelContextProtocol.Server.McpServer` (the concrete implementation behind `IMcpServer`). The package is `ModelContextProtocol.Core` 1.1.0; xmldoc lives at `~/.nuget/packages/modelcontextprotocol.core/1.1.0/lib/net10.0/ModelContextProtocol.Core.xml`. Two overloads are public:

| Overload | Signature (paraphrased) | Notes |
|---|---|---|
| Protocol-level | `Task<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams, CancellationToken ct)` | Direct MCP `sampling/createMessage` shape. Caller fills `Messages` (list of `SamplingMessage`), `MaxTokens`, optional `ModelPreferences`, `IncludeContext`, `StopSequences`, `Metadata`, `SystemPrompt`. Returns `Content` (a `ContentBlock`), `Model`, `StopReason`, `Role`. |
| Microsoft.Extensions.AI bridge | `Task<ChatResponse> SampleAsync(IEnumerable<ChatMessage> messages, ChatOptions chatOptions, JsonSerializerOptions? serializerOptions, CancellationToken ct)` | Wraps the protocol-level call so callers already using `Microsoft.Extensions.AI` types skip the manual mapping. `ChatOptions.MaxOutputTokens` falls back to `McpServerOptions.SamplingDefaults.DefaultMaxOutputTokens` (default 1000) when unset. |

Both overloads throw `InvalidOperationException` when **the client does not declare the `sampling` capability**, and `McpException` when the request fails or the client returns an error. There is also `SampleAsTaskAsync(...)` (long-running variant tied to the SDK's task-store machinery), and `AsSamplingChatClient(...)` which returns an `IChatClient` wrapper for callers who prefer DI-injectable shapes.

**Plumbing requirement.** `McpServer` is the per-connection server instance; tool methods get to it via the `IMcpServer` parameter the SDK injects when present. Today no `[McpServerTool]` in this repo declares an `IMcpServer` (or a derived `RequestContext<CallToolRequestParams>`) parameter — every tool takes services + DTOs only. **Adding sampling to any single tool requires plumbing `IMcpServer` into that tool's signature first.** This is the deepest single piece of friction the spike found.

A spike-time prototype was *not* run end-to-end, because:

1. The cold-context subagent has `toolPolicy = edit-only` and **cannot mutate `src/`** within this PR's scope (the row's `do` text is explicit: "no `src/` changes; investigation only").
2. The proxy used to substitute for "real" cost/latency is the agent's own outer-loop call (this conversation's model invocations), which is the same Claude family the candidate sampling calls would run against anyway. Deltas vs the agent's outer-loop are estimated from published Anthropic pricing + observed latency on prior tool sessions, not measured directly.

Therefore the cost/latency numbers below are **modelled estimates with stated assumptions**, not measured prototypes. This is a deliberate scope discipline (the row caps the spike at "half-day, no production code"), not a measurement gap to fix later — the per-candidate follow-up rows for `go` verdicts will run real prototypes inside their own PR.

---

## (b) Candidate prototypes

Three candidate tool flows from the row:

1. **Auto-XML-doc generation** — generate `<summary>`, `<param>`, `<returns>`, `<exception>` content from a method signature + nearby call-graph snippet at server-side, returned in the tool's response so the agent doesn't have to re-author it in its outer loop.
2. **Refactor-summary text from `workspace_changes`** — turn a session's `WorkspaceChangeDto[]` ("rename_apply: 5 files; extract_method_apply: 1 file; ...") into a 1–2 sentence narrative for commit messages and PR bodies.
3. **Auto-test-name generation** — given a target method name + sibling-test-class context, generate a Given/When/Then test method name (e.g. `LoadIntoSessionAsync_WhenCacheMiss_FallsThroughToColdLoad`) instead of today's `<MethodName>_Needs_Test` placeholder.

### Candidate 1 — Auto-XML-doc generation

**Today's outer-loop equivalent.** The agent calls `document_symbols` + `symbol_info` + `find_references` + `callers_callees`, reads the resulting context, and authors XML doc text inline. The `/document` skill is the canonical prompt-driver. End-to-end on a typical method this is **~6 tool calls + ~600–1500 tokens of agent thinking** before `apply_text_edit` writes the doc.

**Sampled equivalent.** A new `document_member_preview` tool would call `IMcpServer.SampleAsync` with: (a) the canonical signature from `symbol_info`, (b) up to 3 caller snippets from `find_references`, (c) the method body if short enough, (d) a system prompt that constrains output to the XML-doc shape. The tool returns the assembled doc-comment string in the same response shape today's `apply_text_edit`-preview returns.

| Metric | Estimate | Basis |
|---|---|---|
| **Per-call cost** | $0.003–$0.012 | ~3K–8K input tokens (signature + 3 caller snippets + system prompt) × Claude Sonnet 4.5 input rate; ~150–400 output tokens for the XML block. |
| **Per-call latency** | 1.5–4.0s | Sonnet 4.5 P50 first-token ~0.5s + ~200 tokens output × ~10ms/tok. |
| **Quality vs outer-loop** | **Equivalent or worse.** | The outer-loop today already *does* sample with full agent context; moving the sampling server-side just changes where the call originates. The sampled version sees less context (no project-level diagnostics, no editorconfig, no surrounding-type knowledge unless explicitly bundled), so quality is bounded by what the tool stuffs into the prompt. |

**Verdict: NO-GO.** The candidate's value proposition is "skip the agent's outer-loop turn by generating the doc server-side." But the agent's outer loop already runs the same model and has *more* context (the conversation, tool-call history, repo-level conventions) than what the tool can practically marshal into a single sampled prompt. Sampling here moves the cost without reducing it, and the quality ceiling is lower because the sampled prompt is context-starved relative to the agent.

A useful sub-direction *would* be: have the tool emit a **structured "prompt-pack" contextualized for the agent** (signature + callers + sibling-doc style examples), and let the agent's outer loop drive the doc authoring with that pack pre-assembled. That's a `get_documentation_context` read-only tool, **not** a sampling-driven flow. File as a follow-on `tier-1` row only if the manual prompt-assembly cost in `/document` becomes a friction point — current evidence does not justify it.

### Candidate 2 — Refactor-summary text from `workspace_changes`

**Today's outer-loop equivalent.** When the agent commits or opens a PR, it calls `workspace_changes` (returns ordered `WorkspaceChangeDto[]` with description/affectedFiles/toolName/timestamp), reads the array, and authors a 1–2 sentence summary directly in the commit message. End-to-end this is **1 tool call + ~50–200 tokens of agent thinking** for a typical 3–10 change session.

**Sampled equivalent.** A `workspace_changes_summary` tool would call `SampleAsync` with the change list and a "narrate this in 1–2 sentences" system prompt, return the prose. Saves the agent ~50–200 tokens.

| Metric | Estimate | Basis |
|---|---|---|
| **Per-call cost** | $0.002–$0.005 | ~1K–3K input tokens (change list + system prompt) × Sonnet 4.5 input; ~50–100 output tokens. |
| **Per-call latency** | 1.0–2.5s | Sub-200-token outputs are dominated by first-token. |
| **Quality vs outer-loop** | **Worse.** | The sampled version cannot see *why* the changes were made — the agent's outer loop has the original task context ("user asked us to extract the X interface and rename Y → Z"), the sampled call only sees post-hoc tool-name + file-path tuples. Result: bland boilerplate ("Modified 5 files via rename_apply and extract_interface_apply") vs. the agent's intent-aware narrative. |

**Verdict: NO-GO.** The data shape (`WorkspaceChangeDto`) is intentionally tool-mechanical — sequence number, tool name, files. The narrative value is in the *intent* the agent already holds. Moving narration server-side strips that intent. The agent's outer-loop call is already cheap (50–200 tokens) and produces strictly better text. Sampling adds latency for no quality gain.

The cited row evidence for this candidate would only flip to `go` if a **non-agentic consumer** (e.g. a CI bot polling `workspace_changes` to auto-generate release-note bullets) emerged. None has. Not filing a follow-on row.

### Candidate 3 — Auto-test-name generation

**Today's outer-loop equivalent.** `scaffold_test_preview` returns a stub method named `<TargetMethodName>_Needs_Test` (literally — see `ScaffoldingService.cs:1885`). The agent renames it post-scaffold based on the test's intent. For a 5–8-test scaffolding pass, this is **5–8 rename cycles** in the agent loop, each ~50 tokens of thinking + 1 `rename_apply` per stub.

**Sampled equivalent.** During `scaffold_test_preview`, the tool calls `SampleAsync` with: (a) the target method's signature, (b) the target type's surrounding members, (c) up to 2 sibling-test-class examples (the same `*Tests.cs` files the scaffolder already inspects for class-attribute pattern inference — line 90 of the existing description). System prompt: "Suggest a Given/When/Then test method name in `<MethodName>_When<Condition>_<ExpectedResult>` format." Replace the `_Needs_Test` placeholder.

| Metric | Estimate | Basis |
|---|---|---|
| **Per-call cost** | $0.001–$0.003 | ~500–1500 input tokens (signature + sibling-test-class snippet); ~10–30 output tokens for the test name. |
| **Per-call latency** | 0.6–1.2s | Tiny output, dominated by first-token. |
| **Quality vs outer-loop** | **Equivalent to better.** | The agent's renames today are quality-equivalent but cost N round-trips for an N-test scaffold. The sampled call sees the same context the agent uses for renaming (target signature + sibling-test-class examples) — it's a faithful replication of the outer-loop step, packaged as one sampled call instead of N agent turns. The 5–8 rename cycles collapse to 1 sampled call per scaffold; the sibling-pattern inference the scaffolder already does carries directly into the prompt. |

**Verdict: GO.**

Why this one and not the other two: candidate 3 is **bounded** (single short-string output, narrow context, no project-level reasoning needed) and **eliminates a tight loop** (N renames per N scaffolds). Candidates 1 and 2 are unbounded (long output, broad context, requires agent intent) and only eliminate single calls. The asymmetry matters.

**Plumbing requirement.** `IMcpServer` injection into `ScaffoldingTools.PreviewScaffoldTest` (and the underlying `IScaffoldingService.PreviewScaffoldTestAsync`). Per the row's bounded-scope rule: "if the prototype reveals deep wiring is needed for any sample call, defer to follow-on rows" — this is the deep wiring. The follow-on row will land both pieces (plumbing + the sample call), gated behind a `useSampling: bool = false` parameter so non-sampling clients see no behavior change.

---

## (c) Verdicts

| Candidate | Verdict | Cost | Latency | Quality vs outer-loop |
|---|---|---|---|---|
| Auto-XML-doc generation | **NO-GO** | $0.003–$0.012/call | 1.5–4.0s | Equivalent or worse (context-starved relative to agent) |
| Refactor-summary from `workspace_changes` | **NO-GO** | $0.002–$0.005/call | 1.0–2.5s | Worse (strips agent intent; data is tool-mechanical) |
| Auto-test-name generation | **GO** | $0.001–$0.003/call | 0.6–1.2s | Equivalent to better; collapses N rename loops to 1 sampled call |

---

## (d) Per-candidate follow-up rows filed

Filed only on `go` verdicts:

- **`scaffold-test-preview-sampled-test-names`** — Tier-1 row to be filed in `ai_docs/backlog.md` in the next backlog edit. Scope: plumb `IMcpServer` into `ScaffoldingTools.PreviewScaffoldTest`, add a sampled `SuggestTestNameAsync` step gated behind `useSampling: bool = false` so the default behavior is unchanged. Validation: round-trip test against a fixture solution that asserts (i) sampling-disabled path returns `_Needs_Test` placeholder unchanged, (ii) sampling-enabled path returns a non-placeholder name when the mock client returns a string.

  **Filing note.** Per-candidate row authoring lands in the orchestrator's reconcile PR (this PR cannot edit `ai_docs/backlog.md` per the orchestrator-owned-files rule). The orchestrator will read this section and add the row at reconcile time.

No rows filed for candidates 1 and 2.

---

## Bounded-scope discipline check

The row's risks (1) and (2) are both invoked here:

- (1) "Spike scope must stay bounded — if the prototype reveals deep wiring is needed for any sample call, defer to follow-on rows rather than do the wiring in the spike." → **Honored.** Candidate 3's `IMcpServer` plumbing is deferred to its follow-on row, not done in this PR.
- (2) "Don't produce false-positive 'go' for candidates whose context advantage is marginal — the row's evidence bar is 'clear value' not 'could maybe work'." → **Honored.** Candidates 1 and 2 are no-go because their advantage is marginal-or-negative; only candidate 3 (which collapses N loops to 1) cleared the "clear value" bar.

No tool body changes in this PR. Closes `sampling-driven-tool-flows-spike`.
