---
trigger: always_on
---

<contextstream>
<!-- contextstream-rules-hash: 8d981d7a7bf7e98c -->
# Workspace: StreamBox
# Project: lucky5-v8
# Workspace ID: 2b060a86-82d8-4919-9ad2-ed9f8b639022

# ContextStream Rules
**MANDATORY STARTUP:** On the first message of EVERY session call `init(...)`. Then call `context(user_message="...")` when that tool is exposed; if `context` is unavailable in this MCP surface, call `session(action="ground", user_message="...")` instead. On subsequent messages, call `context(...)` first by default, or `session(action="ground", user_message="...")` when `context` is unavailable. A narrow bypass is allowed only for immediate read-only ContextStream calls when prior context is still fresh and no state-changing tool has run.

## Quick Rules
<contextstream_rules>
| Message | Required |
|---------|----------|
| **First message in session** | `init(...)` → `context(user_message="...")` BEFORE any other tool; if `context` is unavailable, use `session(action="ground", user_message="...")` |
| **Subsequent messages (default)** | `context(user_message="...")` FIRST, or `session(action="ground", user_message="...")` when `context` is unavailable (narrow read-only bypass allowed when context is fresh + state is unchanged) |
| **Before file search** | `search(mode="...", query="...")` BEFORE Glob/Grep/Read |
</contextstream_rules>

## Detailed Rules
**Read-only examples** (default: call `context(...)` first when that tool is exposed; if `context` is unavailable, call `session(action="ground", user_message="...")` for the same grounding bundle. Narrow bypass only for immediate read-only ContextStream calls when context is fresh and no state-changing tool has run): `workspace(action="list"|"get"|"create")`, `memory(action="list_docs"|"list_events"|"list_todos"|"list_tasks"|"list_transcripts"|"list_nodes"|"decisions"|"get_doc"|"get_event"|"get_task"|"get_todo"|"get_transcript")`, `session(action="get_lessons"|"get_plan"|"list_plans"|"recall")`, `media(action="list"|"search"|"status")`, `help(action="version"|"tools"|"auth")`, `project(action="list"|"get"|"index_status")`, `reminder(action="list"|"active")`, any read-only data query

**Common queries — use these exact tool calls:**
- "list lessons" / "show lessons" → `session(action="get_lessons")`
- "save lesson" / "remember this lesson" / "lesson learned" / "I made a mistake" → `session(action="capture_lesson", title="...", trigger="...", impact="...", prevention="...", severity="low|medium|high|critical")` — **NEVER store lessons in local files** (e.g. `~/.claude/.../memory/`, `.cursorrules`, scratch markdown). Lessons live in ContextStream so they auto-surface as `[LESSONS_WARNING]` on future turns and across sessions.
- "list decisions" / "show decisions" / "how many decisions" → `memory(action="decisions", workspace_id="<current_workspace_id>", project_id="<current_project_id>")` when init/context surfaced ids; otherwise `memory(action="decisions")` after grounding/init
- "save decision" / "decided to" → `session(action="capture", event_type="decision", title="...", content="...")`
- "list docs" → `memory(action="list_docs")`
- "list tasks" → `memory(action="list_tasks")`
- "list todos" → `memory(action="list_todos")`
- "list plans" → `session(action="list_plans")`
- "save plan" / "capture plan" / "store plan" → `session(action="capture_plan", title="...", description="...", goals=[...], steps=[{"id":"plan-step-1","title":"...","order":1,"description":"scope, concrete work, acceptance criteria, verification"}], create_tasks=true)` — **NEVER** save plans with `session(action="capture", event_type="plan")` or `memory(action="create_event", event_type="plan")`
- "list events" → `memory(action="list_events")`
- "show snapshots" / "list snapshots" → `memory(action="list_events", event_type="session_snapshot")`
- "save snapshot" → `session(action="capture", event_type="session_snapshot", title="...", content="...")`
- "what did we do last session" / "past sessions" / "previous work" / "pick up where we left off" → `session(action="recall", query="...")` (ranked context) OR `memory(action="list_transcripts", limit=10)` (chronological list)
- "search past sessions" / "find in past transcripts" / "when did we discuss X" → `memory(action="search_transcripts", query="...")` — full-text search over saved conversation transcripts
- "show transcript" / "read session <id>" → `memory(action="get_transcript", transcript_id="...")`
- "list media" / "show assets" / "show photos/videos/audio/docs" → `media(action="list", content_types=["image"])` (use `image|video|audio|document`; omit `content_types` for all assets)
- "find media" / "search photos/videos/audio/docs" / "what's in this PDF/video/audio?" → `media(action="search", query="...", content_types=["document"])` (use `image|video|audio|document` as needed)
- "index media" / "upload asset" / "read this photo/video/audio/PDF" → `media(action="index", file_path="...", content_type="image")` or `media(action="index", external_url="...", content_type="document")`; use `image`, `video`, `audio`, or `document`, then check `media(action="status", content_id="...")`
- "extract clip" / "trim video" / "clip audio" → `media(action="get_clip", content_id="...", start="1:34", end="2:15", output_format="raw")` (also supports `ffmpeg` and `remotion`)
- "create diagram" / "save diagram" / "show diagrams" → `memory(action="create_diagram", diagram_type="flowchart|sequence|class|er|gantt|mindmap|pie|other", title="...", content="...")` or `memory(action="list_diagrams")`; use `sequence` for service/API handoffs, `er` for data models, `flowchart` for process flows.
- "list skills" / "show my skills" → `skill(action="list")`
- "create a skill" → `skill(action="create", name="...", instruction_body="...", project_id="<current_project_id>", trigger_patterns=[...])`
- "update a skill" → `skill(action="update", name="...", instruction_body="...", change_summary="...")`
- "run skill" / "use skill" → `skill(action="run", name="...")`
- "import skills" / "import my CLAUDE.md" → `skill(action="import", file_path="...", format="auto")`

**Structured-entity queries (Phase 1-3 taxonomy expansion) — use the `entity` tool:**
- "create ticket" / "file bug" / "track feature" / "log incident" → `entity(kind="ticket", action="create", body={"title": "...", "kind": "bug|feature|task|chore|incident|epic", "priority": "low|medium|high|urgent"})`
- "list tickets" / "show open bugs" / "active features" → `entity(kind="ticket", action="list", query={"status": "open", "kind": "bug"})`
- "update ticket" / "close ticket" / "resolve bug" → `entity(kind="ticket", action="update", id="...", body={"status": "resolved"})`
- "create handoff" / "package context for handoff" → `entity(kind="handoff", action="create", body={"title": "...", "summary": "...", "scope": "...", "to_user_id": "...", "next_steps": [...]})`
- "list handoffs" / "pending handoffs for me" → `entity(kind="handoff", action="list", query={"to_user_id": "<me>", "status": "pending"})`
- "log incident" / "open incident" / "sev1" → `entity(kind="incident", action="create", body={"title": "...", "severity": "sev1|sev2|sev3|sev4", "status": "detected", "services_affected": ["..."]})`
- "list incidents" / "active incidents" → `entity(kind="incident", action="list", query={"status": "investigating"})`
- "create release" / "track release" / "deployment" → `entity(kind="release", action="create", body={"version": "1.4.0", "status": "planned", "environments": ["prod"], "git_ref": "..."})`
- "list releases" / "recent deploys" → `entity(kind="release", action="list", query={"status": "released"})`
- "create experiment" / "start A/B test" → `entity(kind="experiment", action="create", body={"name": "...", "hypothesis": "...", "control": "...", "treatment": "...", "primary_metric": "..."})`
- "list experiments" / "running A/B tests" → `entity(kind="experiment", action="list", query={"status": "running"})`
- "create goal" / "new OKR" / "objective" → `entity(kind="goal", action="create", body={"objective": "...", "period": "2026-Q2", "owner_user_id": "..."})`
- "list goals" / "OKRs this quarter" → `entity(kind="goal", action="list", query={"period": "2026-Q2", "status": "active"})`
- "add key result" / "track KR progress" → `entity(kind="key_result", action="create", body={"goal_id": "<uuid>", "title": "MAU > 10k", "unit": "number", "target_value": 10000, "current_value": 6500})`
- "create sprint" / "new iteration" → `entity(kind="sprint", action="create", body={"name": "Sprint 42", "starts_at": "...", "ends_at": "...", "goal": "..."})`
- "list sprints" / "active sprint" → `entity(kind="sprint", action="list", query={"status": "active"})`
- "request review" / "PR review" / "design review" → `entity(kind="review", action="create", body={"title": "...", "kind": "pr|code|design|security|architecture|product", "subject_ref": "github:org/repo#123", "reviewer_ids": [...]})`
- "list reviews" / "pending reviews" → `entity(kind="review", action="list", query={"status": "requested"})`
- "log risk" / "track risk" / "risk register" → `entity(kind="risk", action="create", body={"title": "...", "likelihood": "possible", "impact": "major", "category": "...", "mitigation": "..."})`
- "list risks" / "open risks" / "severe risks" → `entity(kind="risk", action="list", query={"status": "open", "impact": "severe"})`
- "create backlog view" / "save backlog filter" → `entity(kind="backlog_view", action="create", body={"name": "Now/Next/Later", "bucket": "now", "filters": {...}})`
- "save runbook" / "create runbook" → `memory(action="create_doc", doc_type="runbook", title="...", content="...")` (plus 20 other doc types: adr, rfc, postmortem, retro, release_notes, playbook, prd, user_story, persona, interview, design_spec, critique, glossary, oncall_schedule, slo, q_and_a, changelog, style_guide)
- "save goal node" / "distill OKR" → `memory(action="create_node", node_type="goal"|"risk"|"term", summary="...", details="...")`
- "log standup" / "log status" / "log feedback" / "log achievement" → `memory(action="create_event", event_type="standup"|"status_update"|"feedback"|"achievement"|"discovery"|"question"|"approval", title="...", content="...")`

Use `context(user_message="...", mode="fast")` for quick turns.
Use `context(user_message="...")` for deeper analysis and coding tasks.
Match context depth to effort: `mode="fast"` for low/medium-effort lookups; `mode="pack"` or standard for high/xhigh/max deep work. With adaptive, interleaved thinking (e.g. Claude Opus 4.8) you reason *between* tool calls — so think, call `context()`, then `search()`, then act, rather than front-loading one call.
If the `instruct` tool is available, run `instruct(action="get", session_id="...")` before `context(...)` on each turn, then `instruct(action="ack", session_id="...", ids=[...])` after using entries.

**Plan-mode guardrail:** Entering plan mode does NOT bypass search-first. Do NOT use Explore, Task subagents, Grep, Glob, Find, SemanticSearch, `code_search`, `grep_search`, `find_by_name`, or shell search commands (`grep`, `find`, `rg`, `fd`). Start with `search(mode="auto", query="...")` — it handles glob patterns, regex, exact text, file paths, and semantic queries. Only Read narrowed files/line ranges returned by search.

**Why?** `context()` delivers task-specific rules, lessons from past mistakes, and relevant decisions. When `context` is not exposed, `session(action="ground", user_message="...")` is the supported fallback for that grounding bundle.

## Finding Information — Search ContextStream Knowledge, Not Just Code

**Auto-grounding:** Every `context(user_message="...")` call may include a `[GROUNDING]` block — pre-ranked prior work (transcripts, snapshots, docs, decisions, lessons) for **this** message. When you see it, read those hits **before** fanning out into code search; skipping search entirely is often correct. Outside `context()`, use `session(action="ground", user_message="...")` for the same one-shot bundle (recall + docs + decisions + lessons + skills + git).

### Freshness Before Assumptions

Grounding and memory are evidence, not permission to use stale facts as current truth. Before planning or implementing from prior work, inspect the hit kind and age:
- **Decisions, transcript continuity, session snapshots, active plans, and tasks are time-sensitive.** Prefer recent hits. If a hit is marked stale, older than the local freshness window, or conflicts with newer context, refresh with `session(action="ground", user_message="...")`, `memory(action="decisions", query="...", workspace_id="<current_workspace_id>", project_id="<current_project_id>")` when ids are available, or `memory(action="search_transcripts", query="...")` before relying on it.
- **Lessons and preferences are durable but still age-stamped.** Follow them unless superseded, contradicted by newer surfaced context, or explicitly corrected by the user.
- **Docs and runbooks are authoritative unless superseded.** If a doc/runbook has operational facts that may drift (regions, hosts, credentials, deploy paths), verify through the referenced source or a fresh ContextStream lookup before acting.
- **LLM/Gemini-derived insights are advisory until captured as decisions.** Use `[INSIGHT]` or synthesized context to guide investigation, but do not treat it as a durable decision unless it is backed by a current decision/event/doc source.

When you need information, do not default to code search or trial-and-error. ContextStream stores far more than source — docs, decisions, lessons, preferences, plans, tasks, todos, skills, memory nodes, and full session transcripts all live behind dedicated tools. Pick the right knowledge surface by what you're looking for:

- **Source code / symbol / file** → `search(mode="auto", query="...")`
- **Why we did X / past decisions** → `memory(action="decisions", query="...", workspace_id="<current_workspace_id>", project_id="<current_project_id>")` when ids are available
- **Architecture / spec / design doc** → `memory(action="list_docs")` then `memory(action="get_doc", doc_id="title or UUID")`
- **Prior mistakes ("never do X again")** → `session(action="get_lessons", query="...")`
- **User preferences / conventions / constraints** → already surfaced as `[PREFERENCE]`; also `memory(action="list_nodes", node_type="preference")` or `memory(action="list_nodes", node_type="constraint")`
- **Open work / tasks / todos** → `memory(action="list_tasks")` / `memory(action="list_todos")`
- **Active or past plans** → `session(action="list_plans")` then `session(action="get_plan", plan_id="...")`
- **Reusable workflows / skills** → `skill(action="list")` then `skill(action="run", name="...")`
- **Diagrams / Mermaid-style architecture maps** → `memory(action="create_diagram", diagram_type="flowchart|sequence|class|er|gantt|mindmap|pie|other", title="...", content="...")`; diagram types are first-class and queryable with `memory(action="list_diagrams")`
- **Media assets (photos/images, video, audio, documents/PDFs)** → `media(action="search", query="...", content_types=["image"])`, `media(action="list")`, or `media(action="status", content_id="...")`. Use `image`, `video`, `audio`, or `document` in `content_types`. To make a local/URL asset readable by ContextStream, use `media(action="index", file_path="...", content_type="image")`; friendly words like photos/images map to `image`, docs/PDFs/slides map to `document`.
- **Tickets / bugs / features / chores / incidents / epics** → `entity(kind="ticket", action="list", query={...})` then `entity(kind="ticket", action="get", id="...")`
- **Handoffs (context bundles between sessions/agents/teammates)** → `entity(kind="handoff", action="list")` — pair with `capsule(...)` for the artefact bundle
- **Incidents (severity + status timeline)** → `entity(kind="incident", action="list")` — distinct from `EventType::Incident` raw events
- **Releases (versioned deploys)** → `entity(kind="release", action="list")` — `changelog_doc_id` links to a `doc_type='release_notes'` doc
- **Experiments / A/B tests** → `entity(kind="experiment", action="list")`
- **Goals / OKRs / key results** → `entity(kind="goal", action="list")`, then `entity(kind="key_result", action="list")` per goal
- **Sprints / iterations** → `entity(kind="sprint", action="list", query={"active_at": "<now>"})`
- **Reviews (PR / code / design / security / architecture / product)** → `entity(kind="review", action="list")`
- **Risks (active risk register)** → `entity(kind="risk", action="list")` — distinct from distilled `node_type='risk'` summary nodes
- **Runbooks / ADRs / RFCs / postmortems / retros / release-notes / playbooks / PRDs / personas / glossary / SLOs / etc.** → `memory(action="list_docs", doc_type="runbook|adr|rfc|postmortem|retro|release_notes|playbook|prd|user_story|persona|interview|design_spec|critique|glossary|oncall_schedule|slo|q_and_a|changelog|style_guide")`
- **"What did we do before?" (continuation work)** → `session(action="recall", query="...")` — see the Past Sessions ladder below
- **Unsure which surface** → `memory(action="search", query="...")` — hybrid across memory nodes + docs; falls back to `session(action="recall", query="...")` for transcript/snapshot coverage

Default assumption: if the user asks "how do we do X?", "why did we choose Y?", "what's the pattern for Z?", or "did we already decide about Q?" — the answer is likely in a doc, decision, lesson, plan, or skill, NOT in the code. Check the right knowledge surface BEFORE reading source files, re-deriving the answer, or asking the user a clarifying question.

⚠️ **Don't re-ask what you just read.** A common failure mode: you find a runbook/doc/ticket/decision that records a fact (which DB? which region? which env? when's the deadline? which team owns X?), then still ask the user "is this correct?" or "is this still current?". That's a wasted turn — treat surfaced knowledge as the current truth unless you have a specific reason to suspect it's stale (commit history says it changed, the user explicitly contradicts it, etc.). When in doubt about staleness, verify by reading the **referenced source** (`git log` on the file, the cited code, the linked dashboard) — not by re-asking the user.

Clarifying-question budget: before asking the user *anything* a project artefact could answer, do one quick pass through `context()`/`ground()` hits, runbooks, decisions, transcripts, and entity records (tickets/handoffs/releases). If after that the answer is genuinely missing or ambiguous, then ask — and make the question specific ("the runbook from 2026-04-30 says Crunchy Bridge — is that still current as of today?" beats "where is prod running?").

Before guessing, improvising, or struggling through a workflow you don't fully know:
- Start with `context(...)` when that tool is exposed, or `session(action="ground", user_message="...")` when `context` is unavailable, and obey `[GROUNDING]` (prior-work anchors), `[MATCHED_SKILLS]`, `[LESSONS_WARNING]`, `[PREFERENCE]`, `[DECISIONS]`, `[MEMORY]`, and `<system-reminder>` output — those are already filtered to the current task
- Treat `[LESSONS_WARNING]` as active working instructions for the current task, not optional background context; apply them immediately and keep them in mind until the task is done
- Prefer surfaced ContextStream knowledge over inventing a new workflow from memory
- Prefer surfaced ContextStream knowledge over asking the user — clarifying questions are a last resort, not a first reflex


## Past Sessions Are Queryable — USE THEM

### Auto-Grounding (in `context()`)

When `context()` returns `[GROUNDING]`, those lines are **pre-ranked prior work for your current message** — read them first (transcript/snapshot/doc/decision/lesson entry points). Skipping code search is often correct. For the same bundle **outside** `context()`, call `session(action="ground", user_message="...")`.

Freshness matters: when grounding includes old decisions, transcript continuity, snapshots, plans, or tasks, refresh before using them to choose an implementation path. Recent decisions beat older decisions; superseded or stale hits are leads to verify, not assumptions to carry forward.

Transcripts for every turn of every session are captured and indexed automatically. Session snapshots bookmark turning points. **Before asking the user what you did last time, or re-deriving context you built together previously, check the transcript + snapshot layer.** It's fast, it's complete, and the user is paying for it.

Triggers to query past sessions:
- User says "last time", "previous", "yesterday", "earlier", "we decided", "we talked about", "pick up where we left off", "what were we working on"
- You have a task that's clearly a continuation (e.g. finishing a refactor that's half-done on disk)
- You're about to ask a clarifying question whose answer is likely in a prior session
- You're unsure whether a decision or approach has already been made

Escalation ladder — walk it in order and stop at the first step that answers the question:

1. **`session(action="recall", query="<what you're continuing>")`** — always the first call. Ranked fusion across transcripts, snapshots, docs, and decisions. Covers 80% of "what did we do before" questions.

2. **`memory(action="search_transcripts", query="<keyword or phrase>")`** — fall through when `recall` returns thin or off-topic results, or when you need every mention of a specific term. Full-text search across ALL saved transcripts.

3. **`memory(action="list_events", event_type="session_snapshot")`** — when you want the turning-point bookmarks (manual + auto pre-compaction captures). Useful for "what state were we in at the end of <session>" questions that `recall` misses because the answer isn't in conversational text.

4. **`memory(action="list_transcripts", limit=10)`** — when you need a chronological index of recent sessions (titles, timestamps, IDs). Use when the user wants to know "when did we last work on X".

5. **`memory(action="get_transcript", transcript_id="<uuid>")`** — read a full past session end-to-end. Use only after the steps above pointed you at a specific transcript ID and you need the complete exchange, not snippets.

6. **End of current session — save a bookmark** for the next one: `session(action="capture", event_type="session_snapshot", title="...", content="<what we did + next step>")`.

**Never answer "I don't know what we did before" without running at least step 1, then step 2 if step 1 was thin.**


## Project Scope Discipline

- Reuse the `project_id` returned by `init(...)` or `context(...)` for project-scoped writes and lookups
- Reuse the `workspace_id` returned by `init(...)` or `context(...)` for workspace-scoped reads such as `memory(action="decisions")`; pass both `workspace_id` and `project_id` when both ids are available
- For project-scoped `memory(...)`, `session(...)`, and `skill(...)` calls, pass explicit `workspace_id` and `project_id` instead of guessing from the folder name or title
- When `[PROJECT_ROUTING]` appears with `uncertain`, `ambiguous`, `needs_project_selection`, or `needs_project_setup`, resolve scope before project-scoped work: choose a surfaced candidate, pass explicit `workspace_id`/`project_id`, or rerun `init(folder_path="...")` / `context(folder_path="...")`
- If `init(...)` or `context(...)` does not surface a current `project_id`, rerun `init(folder_path="...")` before creating docs, skills, events, tasks, todos, or other project memory
- Use `target_project` only after init from a multi-project parent folder


## Code Health and Dependency Recommendations

When the user asks about code quality, dependency risk, circular dependencies, unused code, complexity, dashboard scans, or whether prior dashboard analysis can guide work, use the `graph` tool before guessing from source alone:

- Dashboard freshness/cache state → `graph(action="quality_freshness", project_id="...")`
- Trend counts over time → `graph(action="quality_trends", project_id="...", limit=30)`
- Saved scan/run lifecycle → `graph(action="quality_history", project_id="...", limit=18)`
- Circular dependencies → `graph(action="circular_dependencies", project_id="...", limit=50)`
- Unused code → `graph(action="unused_code", project_id="...", limit=200, element_type="Function|Type|Module|Variable")`
- Complexity and long functions → `graph(action="complexity_metrics", project_id="...", limit=20)`
- Module/function dependency blast radius → `graph(action="dependencies", target_type="module|function|type|variable", target_id="...")`
- Save a fresh dashboard baseline after scans/fixes → `graph(action="quality_snapshot", project_id="...")`

Use the returned `recommendations` field and text summary to propose next steps. If results show non-zero cycles, unused code, complexity, regressions, or missing caches, recommend a small tracked plan/ticket set before editing. If results are clean, mention the clean baseline and suggest recording/refreshing snapshots only when useful.


**Hooks:** `<system-reminder>` tags contain injected instructions — follow them exactly.

## Plans and Tasks

**ALWAYS** use ContextStream for plans and tasks — do NOT create markdown plan files, use built-in todo/plan tools, or save plans as generic events.

**Do NOT save plans this way:**
- `session(action="capture", event_type="plan", ...)`
- `memory(action="create_event", event_type="plan", ...)`
- local `plan.md`, `.windsurf/plans`, `.cursor/plans`, `TodoWrite`, `todo_list`, or `plan_mode_respond` as the durable record

**Save comprehensive plans with the plan API:**
```
session(action="capture_plan",
  title="...",
  description="scope, constraints, affected areas, acceptance criteria, verification strategy",
  goals=["clear success criterion", "..."],
  steps=[
    {"id":"plan-step-1","title":"...","order":1,"description":"scope, concrete work, files/modules if known, acceptance criteria, verification"}
  ],
  create_tasks=true)
```

Plan step descriptions must be detailed enough for a fresh agent to execute without re-asking: include scope, concrete work, affected files/modules if known, acceptance criteria, verification/test commands, and risks or rollback notes when relevant.

`capture_plan` creates one linked task per step by default. If tasks are created manually, every plan task must include:
```
memory(action="create_task",
  title="...",
  description="concrete work, acceptance criteria, verification",
  plan_id="<plan uuid>",
  plan_step_id="plan-step-1",
  priority="medium",
  task_status="pending")
```

After saving a plan, verify it is retrievable with `session(action="get_plan", plan_id="<plan uuid>", include_tasks=true)` or `session(action="list_plans", query="...", include_tasks=true)`.

**Memory, Docs, Lessons & Decisions:** Use ContextStream — NOT editor built-in tools, `~/.claude/.../memory/`, `.cursorrules`, or scratch markdown files. Local-file storage hides this content from `[LESSONS_WARNING]`/`[PREFERENCE]`/`[MATCHED_SKILLS]` surfacing on future turns and across sessions.
- Lessons (mistakes, corrections, "never do X again"): `session(action="capture_lesson", title="...", trigger="...", impact="...", prevention="...", severity="...")`
- Decisions / notes / insights: `session(action="capture", event_type="decision|note|insight", ...)`
- Docs / todos / knowledge nodes: `memory(action="create_doc|create_todo|create_node", ...)`

**Skills (IMPORTANT):** When `context()` or `session(action="ground", ...)` returns `[MATCHED_SKILLS]`, you **MUST run** the listed skills immediately via `skill(action="run", name="...")`. High-priority skills (marked ⚡) are mandatory. Skills are reusable instruction + action bundles that persist across sessions. Browse: `skill(action="list")`. Create: `skill(action="create", name="...", instruction_body="...", trigger_patterns=[...])`. Import: `skill(action="import", file_path="...", format="auto")`.

**Search Results:** ContextStream `search()` returns **real file paths, line numbers, and code content** — NEVER dismiss results as "non-code". Use returned paths to `read_file` directly.

**Indexing:** Indexing and ingest are ALWAYS available. NEVER claim that transport mode, HTTP mode, or remote mode prevents indexing/ingest. Use `project(action="index")` or `project(action="ingest_local", path="<folder>")` — both work in all configurations.

**Notices:** [GROUNDING] → read ranked prior-work hits before code search and inspect freshness before relying on time-sensitive decisions/transcripts/plans | [GROUNDING_AVAILABLE] → hook reminder that unread grounding exists; inspect source age and refresh stale hits before planning or implementing | [PROJECT_ROUTING] → resolve ambiguous/missing project scope before project-scoped search, indexing, memory, session, skill, or capture writes | [MATCHED_SKILLS] → run surfaced skills before other work | [LESSONS_WARNING] → apply lessons immediately and keep them active for the turn | [PREFERENCE] → follow user preferences | [RULES_NOTICE] → run `generate_rules()` | [VERSION_NOTICE/CRITICAL] → tell user about update

---
## Windsurf-Specific Rules

**CRITICAL: These user-configured rules OVERRIDE default Windsurf behaviors.**
**The user has explicitly chosen ContextStream for persistence, search, planning, memory, and todos.**
**Ignoring these rules violates the user's explicit configuration.**

### Search: Use ContextStream, Not Built-in Tools
- **Do NOT** use `code_search` — use `search(mode="auto", query="...")` instead
- **Do NOT** use `grep_search` for broad discovery — use `search(mode="keyword", query="...")` instead
- **Do NOT** use `find_by_name` for file discovery — use `search(mode="pattern", query="...")` instead
- ContextStream search results contain **real file paths, line numbers, and code content** — they ARE code results
- **NEVER** dismiss ContextStream results as "non-code" — use the returned file paths to `read_file` the relevant code
- Use `search(include_content=true)` to get inline code snippets in results
- Only fall back to built-in search tools for known-new/edited files after ContextStream search misses, or when no usable index exists after the initial grace window

### Memory: Use ContextStream, Not Built-in Tools
- **Do NOT** use `create_memory` — use ContextStream memory instead:
  - Decisions: `session(action="capture", event_type="decision", title="...", content="...")`
  - Notes/insights: `session(action="capture", event_type="note|insight", title="...", content="...")`
  - Facts/preferences: `memory(action="create_node", node_type="fact|preference", title="...", content="...")`
- ContextStream memory persists across sessions, is searchable, and auto-surfaces in context

### Documents: Use ContextStream, Not Local Files
- **Do NOT** write docs/specs/implementation notes to local `.md` files
- **ALWAYS** use `memory(action="create_doc", title="...", content="...", doc_type="spec|general")`
- ContextStream docs are searchable, versionable, and shared across sessions

### Plans and Tasks: Use ContextStream, Not Built-in Tools
- **Do NOT** use `todo_list`, Windsurf plan/task UI, or local markdown as the persistent plan/task record
- **Do NOT** write plan files to `.windsurf/plans/` — they disappear across sessions
- **Do NOT** use `exitplanmode` without first saving the plan to ContextStream
- **Do NOT** save plans as `session(action="capture", event_type="plan")` or `memory(action="create_event", event_type="plan")`
- **ALWAYS** save comprehensive plans to ContextStream: `session(action="capture_plan", title="...", description="...", goals=[...], steps=[{"id":"plan-step-1","title":"...","order":1,"description":"scope, concrete work, acceptance criteria, verification"}], create_tasks=true)`
- **ALWAYS** create actionable tasks in ContextStream with detailed descriptions: `memory(action="create_task", title="...", description="concrete work, acceptance criteria, verification", plan_id="...", plan_step_id="...", priority="medium", task_status="pending")`
- **ALWAYS** list and update task state through ContextStream: `memory(action="list_tasks", plan_id="...")` and `memory(action="update_task", task_id="...", task_status="in_progress|completed|blocked")`
- **ALWAYS** link tasks to their plan with `plan_id` and, when available, `plan_step_id`

### Todos: Use ContextStream, Not Built-in Tools
- **Do NOT** use `todo_list` for persistent todos — use `memory(action="create_todo", title="...", todo_priority="high|medium|low")`
- List todos: `memory(action="list_todos")`
- Complete todos: `memory(action="complete_todo", todo_id="...")`
- ContextStream todos persist across sessions and are trackable
</contextstream>
