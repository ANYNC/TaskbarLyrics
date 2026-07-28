---
name: taskbarlyrics-webview-ui-auditor
description: Performs read-only audits of TaskbarLyrics WebView2 interfaces implemented with native HTML, CSS, and JavaScript, covering visual consistency, accessibility, keyboard and focus behavior, component states, interaction logic, animation, DPI, overflow, performance, and optional end-to-end WebView V1/C# host flows. Use when reviewing, auditing, checking, or evaluating TaskbarLyrics Web UI or UX, including Settings, Lyrics, SmtcMonitor, and SpectrumTuning. Supports a default frontend mode and an explicit end-to-end mode. Never edits files.
---

# TaskbarLyrics WebView UI Auditor

Audit the current on-disk interface and report evidence-backed findings. Remain read-only even if the user asks for suggested fixes; describe changes without applying them.

## Load authority in order

Before inspecting implementation:

1. Read the repository-root `AGENTS.md`.
2. Read `../taskbarlyrics-clean-code-guardian/SKILL.md` and every reference it requires.
3. Read `../../../docs/WebView界面视觉与交互规范.md` completely. Treat it as the final project authority.
4. When the `web-design-guidelines` skill is available, read and apply it, including its current upstream guideline retrieval. Treat online guidance as supplementary when it conflicts with the local standard.

If an online guideline cannot be retrieved, continue with the local authority and disclose the missing external evidence. Do not weaken or invent project rules.

## Select the audit mode

Use **frontend mode** unless the user explicitly requests `end-to-end`, host integration, message flow, persistence, or complete interaction behavior.

### Frontend mode

Inspect the requested WebView HTML, CSS, and JavaScript. If scope is omitted, inspect:

- `TaskbarLyrics.App/Web/Settings`
- `TaskbarLyrics.App/Web/Lyrics`

Follow imported modules and tests that materially explain behavior. Evaluate:

- visual hierarchy and consistency with existing tokens and component patterns;
- semantic HTML, accessible names, ARIA state, live regions, language, and decorative content;
- Tab order, keyboard patterns, focus movement, focus restoration, and hidden/inert content;
- navigation, dialogs, popovers, listboxes, radio groups, sliders, switches, drag sorting, and color controls;
- Hover, Pressed, Selected, Focus, Disabled, Loading, Error, and Empty states;
- event delegation, click-outside, Escape, resize, scroll, blur, and repeated input behavior;
- local state transitions, rendering consistency, stale callbacks, duplicate actions, and recoverability;
- dark/light themes, Windows high contrast, `prefers-reduced-motion`, animation interruption, and overflow;
- DPI-sensitive layout, narrow windows, long localized text, and dynamic media metadata;
- DOM update cost, layout thrashing, timers, observers, animation frames, and resource cleanup;
- missing or overly implementation-coupled Vitest/jsdom coverage.

### End-to-end mode

Perform the frontend audit, then trace each material interaction through:

```text
User input
  -> JavaScript event/state
  -> WebView V1 envelope
  -> C# parser/router
  -> setting, persistence, or Windows operation
  -> host response
  -> JavaScript receive/render
```

Inspect only relevant host files, typically message routers, WebView adapters, owning windows, native interaction/theme services, settings storage, and App/Core tests. Verify:

- `{ version: 1, type, payload }` symmetry and payload validation;
- safe handling of unknown versions, types, malformed payloads, and duplicate messages;
- one source of truth for message types, settings keys, status codes, and UI mappings;
- async failure observation, cancellation, ordering, reentrancy, and duplicate-submit protection;
- WPF/WebView dispatcher boundaries and native DPI/physical-pixel conversions;
- persistence timing, rollback/fallback behavior, and UI confirmation accuracy;
- disposal of WebView resources, timers, observers, event subscriptions, and callbacks;
- tests covering observable success, rejection, failure, and recovery paths.

Do not expand into unrelated domain internals merely because they are reachable. Stop at the boundary needed to prove or disprove the interaction.

## Inspect with evidence

1. Run `git status --short` and note pre-existing changes without modifying them.
2. Read current source rather than relying on screenshots or prior audit conclusions.
3. Trace handlers and state changes before calling an interaction broken.
4. Distinguish confirmed defects, verification gaps, and subjective preferences.
5. Report only issues with a concrete user, accessibility, correctness, consistency, or performance consequence.
6. Do not treat generic browser guidance as mandatory when the local standard documents a WebView2/Windows adaptation.
7. Do not run the app, write snapshots, format files, or update documentation unless the user separately authorizes a non-audit task.

## Grade findings

- **P0**: blocks use, causes data loss, or creates a severe safety/security failure.
- **P1**: prevents an important interaction or makes it inaccessible to a material user group.
- **P2**: causes meaningful inconsistency, unreliable feedback, avoidable performance cost, or a likely edge-case failure.
- **P3**: localized polish, maintainability, or verification gap with limited immediate impact.

Do not inflate severity. A missing optional enhancement is not a defect.

## Produce the report

Lead with findings, ordered by severity and then file. For every actionable finding include:

- `file:line` using the current line number;
- concise title and severity;
- observed code evidence;
- user or engineering consequence;
- the smallest compatible recommendation;
- rule origin: `本地项目规范`, `通用 Web Guidelines`, or `WebView2/Windows 适配`.

Then include:

1. **Interaction coverage** — state whether the audit was frontend or end-to-end and list the traced flows.
2. **Patterns worth preserving** — identify existing tokens, components, and interaction patterns that should not be lost.
3. **Rule classification** — group applied rules into:
   - 可直接纳入项目规范
   - 需要针对 WebView2/Windows 适配
   - 不适用于本项目
4. **Manual verification** — list only checks that require a real Windows/WebView2 session, such as DPI, high contrast, UI Automation, taskbar edge, or multi-monitor behavior.
5. **Scope and evidence gaps** — disclose files, runtime states, or external guidelines that were unavailable.

If no actionable findings exist, say so explicitly and still report coverage and remaining manual checks. Never modify files during the audit.
