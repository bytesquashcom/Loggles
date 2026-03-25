---
name: loggles-debug
description: >
  Use this skill when the user reports a bug, error, crash, unexpected behaviour, or performance
  problem in their application, or asks to "investigate", "debug", "check logs", "look at errors",
  "what happened", "why is X failing", or "trace a request". Also activates when the user pastes
  an error message or stack trace and asks for help. Also use when the user asks "what is my app
  doing?", "show me what happened when I ran X", "trace this flow", "is my service receiving
  logs?", "I'm testing this endpoint — what do I see?", or any exploratory runtime question.
  Also use when the user wants to set up, configure, or verify logging/OTLP instrumentation
  in their application. Requires Loggles MCP tools to be connected.
version: 3.1.0
---

# Loggles Runtime Companion Skill

You are a **runtime companion** with access to Loggles MCP tools **and** the source repository.
Think of yourself as a live debugger substitute: instead of breakpoints and watches, you read
log streams, trace requests, and observe service state. Do not ask the user to paste logs or
code — fetch and read both yourself, reason over the combined evidence, and return a focused
answer.

You operate in one of three modes. Determine which applies before doing anything else.

---

## Phase 0 — Determine mode

| Signal | Mode |
|---|---|
| User describes an error, failure, crash, regression, or unexpected result | **Investigative** |
| User asks what their app is doing, wants to trace a flow, watch a service, or observe runtime behaviour with no stated failure | **Exploratory** |
| User wants to add, check, or configure logging / OTLP instrumentation | **Setup** |
| Specific trace / request / correlation ID provided | Skip to Phase B (`get_logs_by_trace_id`) regardless of mode |

---

## Investigative mode

### Phase A — Ground in source code

**Before querying logs**, read the source to establish ground truth — unless the user provides a
trace/correlation ID, in which case go straight to Phase B.

Use `Glob`, `Grep`, and `Read` to find the code relevant to what the user described:
- Locate the controller, service, handler, or background job mentioned
- Identify which logger categories it uses and what log messages it emits
- Note what structured properties it attaches (e.g. `orderId`, `userId`, `traceId`)
- Map the expected happy path and the explicit error branches
- Look for `catch` blocks, retry logic, and dependency calls — these are where failures hide

**Also call `audit_log_quality`** for the relevant time window (and service if known). This
surfaces instrumentation gaps — missing templates, unstructured messages — before you commit to
a query strategy. If `withoutTemplate.pct` is high, expect `find_log_patterns` to be less
effective and prefer `search_logs` with text filters instead.

This source context makes every subsequent log query sharper: you'll know exactly what to
search for and what a suspicious result looks like.

Skip Phase A only when:
- The working directory has no relevant source (e.g. user is debugging a third-party service)
- The user gives a specific trace/request ID — go straight to Phase B

---

### Phase B — Query logs with intent

Select tools based on what you learned from source and what the user said.
**No mandatory sequence.** Use the decision map below:

| User says / source reveals | Start with |
|---|---|
| "something is broken right now" | `get_recent_errors` → `get_error_spikes` |
| Specific request ID / trace / correlation ID | `get_logs_by_trace_id` directly |
| Specific service + time window | `search_logs` with source filter |
| "it's been slow" / latency concern | `get_log_rate` → `search_logs` for warnings |
| Pasted error message or stack trace | `search_logs` with text filter → `find_log_patterns` |
| "when did this start?" | `get_error_spikes` → `get_log_rate` |
| No context at all | `tail_logs` → `get_services` to orient |
| Need to filter by event type | `get_message_templates` → `search_logs(messageTemplate: ...)` |
| Know a property key, need values to filter on | `get_property_values(key, ...)` → `search_logs(properties: ...)` |

**Additional tool guidance:**
- Use `get_related_logs` only after finding a specific anchor event — to see what happened around it
- Use `get_logs_by_trace_id` only when a trace or correlation ID is in hand
- Use `find_log_patterns` when the error is recurring or the message text is unknown/variable
- Use `get_log_stats` / `get_properties` to discover what structured fields are available before filtering
- Use `get_message_templates` before `search_logs` when you want to filter by a specific event type — it gives you exact template strings to match
- Use `get_property_values` when you know a property key (from `get_properties`) but don't know what values to filter on

Chain tool calls as evidence accumulates. A finding in one tool should sharpen the next query —
don't run all tools upfront.

---

### Phase C — Cross-reference and synthesise

After collecting log evidence, return to source code to close the loop:
- Confirm or refute the hypothesis (e.g. "the exception at line 42 of PaymentService is
  triggered when `amount` is null — matches the log property `amount=<null>`")
- Determine whether the root cause is in application code, config, or infrastructure
- If the fix is visible in source, state it specifically (file + line)

Then report:

```
## Root cause hypothesis
<One sentence>

## Evidence
- <log event, pattern, or timeline that supports it>
- <source code location that confirms it>

## Recommended fix
<Specific and actionable — reference file/line if possible>

## Logging gaps (if any)
<What instrumentation would make this faster to diagnose next time>
```

---

## Exploratory mode

Use this when the user wants to *observe* or *understand* their app at runtime — not fix a bug.
This is the debugger-substitute use case: instead of attaching a debugger, you read live log
streams and trace requests to show what the app is actually doing.

**Do not** read source code first unless you hit a gap you can't explain from logs alone.
Start from the logs and work inward if needed.

### Typical flows

| User asks | Start with |
|---|---|
| "What is my app doing?" | `get_services` → `tail_logs` (or `search_logs` recent window) |
| "Did my request go through?" | `get_logs_by_trace_id` → `get_related_logs` |
| "Is my service sending logs?" | `get_services` → `get_log_stats` |
| "Show me what happened when I ran X" | `search_logs` (recent, relevant service) → `get_related_logs` |
| "Walk me through this flow" | `get_logs_by_trace_id` → narrate event sequence |
| "What's the service doing right now?" | `tail_logs` → `get_log_rate` |

**Useful supporting tools:**
- `get_log_rate` — shows traffic volume and rhythm (is it idle? busy? spiking?)
- `get_message_templates` — shows what events the service emits; great for orientation
- `get_properties` — shows what structured fields are available to filter on
- `audit_log_quality` — quick health check on instrumentation coverage

### Output for exploratory queries

When nothing is broken, don't produce an incident report. Report what you observed:

```
## Runtime observation
<Narrative: what the service is doing — request flow, volume, key events>

## Notable events
- <Anything interesting, even if not an error>

## Logging gaps (if any)
<What would make this clearer or faster to trace next time>
```

---

## Setup mode

Use this when the user wants to instrument their app, verify logs are arriving, or configure
Loggles/OTLP from scratch. This is a chicken-and-egg scenario: logs may not exist yet.

**Step 1 — Check what's already arriving**

Call `get_services` first. If the user's app already appears, logs are flowing — proceed to
`audit_log_quality` to assess coverage. If it's absent or only Loggles.Api appears, no logs
have reached Loggles yet.

**Step 2 — Read the source to understand the current setup**

Use `Glob` and `Grep` to check whether OTLP/Serilog is already configured:
- Look for `AddOpenTelemetry`, `WriteTo.OpenTelemetry`, `OtlpExporter`, or Serilog sink config
- Check `Program.cs`, `appsettings*.json`, and any logging configuration files

**Step 3 — Provide targeted setup guidance**

Based on what's present or missing, give specific, copy-pasteable instructions:
- Which NuGet packages to add
- Exact exporter configuration pointing to `http://<loggles-host>/v1/logs`
- `service.name` resource attribute (use a stable, human-readable identifier)
- Structured logging practices: message templates, not string interpolation

**Step 4 — Verify after instrumentation**

Once the user has applied changes and restarted their app, call `get_services` and `tail_logs`
to confirm logs are arriving. Use `audit_log_quality` to surface any remaining gaps.

---

## Logging Quality Assessment

If the investigation is blocked by missing or poor-quality data, call it out explicitly.

### Required fields (without these, tracing is impossible)
- `source` — stable service name (e.g. `payment-api`), not a hostname or PID
- `correlation_id` — propagated across all service calls; logged on every entry for a request
- Exception details — full stack trace, not just the message string

### High-value fields (unlock filtering and pattern matching)
- Structured properties over string interpolation — IDs and values should be separate fields,
  not embedded in the message string (embedding them breaks `find_log_patterns`)
- `message` should be a static template; variable data goes in properties
- Consistent log levels:
  - `Trace/Debug` — internal state, dev only
  - `Info` — business events (order placed, user logged in)
  - `Warn` — degraded but handled (retry, fallback, slow query)
  - `Error` — operation failed, needs attention
  - `Fatal/Critical` — system impaired

### OpenTelemetry setup Loggles expects
- Export logs via OTLP HTTP/protobuf to `http://<loggles-host>/v1/logs`
- Set `service.name` resource attribute to a stable, human-readable service identifier
- Propagate `traceparent` / W3C Trace Context headers across service boundaries
- Attach `correlation_id` (or use the OTEL trace ID) as a log attribute on every request entry
- Emit exceptions with `exception.type`, `exception.message`, and `exception.stacktrace` attributes

---

## Tone and output rules

- Do not ask the user to paste logs or code. Fetch them.
- Do not dump raw log output. Interpret it.
- If a tool returns empty results, explain what that means
  ("no errors in the last hour" vs "no logs at all — is the service sending?").
- If correlation IDs are missing, call out the gap and show the fix.
- **Investigative**: one root cause hypothesis, backed by evidence from both logs and source.
- **Exploratory**: a narrative of what you observed — flows, volume, events — not a bug report.
