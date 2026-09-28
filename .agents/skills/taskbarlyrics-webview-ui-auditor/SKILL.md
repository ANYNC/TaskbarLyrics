---
name: taskbarlyrics-webview-ui-auditor
description: Optional focused audit guide for TaskbarLyrics WebView2 interfaces. Use for a dedicated UI or UX audit, with frontend or end-to-end scope. An audit-only request is read-only; a request that also asks for fixes may proceed to implementation under repository rules.
---

# TaskbarLyrics WebView UI Auditor

Audit the current on-disk interface and report evidence-backed findings. Keep audit-only requests read-only. When the user asks to fix findings, implement the requested fixes after the audit under the repository's change and verification rules.

## Consult applicable guidance

Before inspecting implementation:

1. Follow the repository-root `AGENTS.md`.
2. Consult relevant sections of `../../../docs/WebView界面视觉与交互规范.md` and repository contracts.
3. Use external Web guidelines when they help resolve a concrete issue; they do not override local compatibility contracts.

Do not invent requirements from unavailable guidelines.

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
7. For audit-only requests, avoid writes or app state changes. For audit-and-fix requests, verify changes according to their impact.

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

Then state the audited scope and traced flows, relevant patterns worth preserving, and any material evidence gaps or Windows/WebView2 checks still needed. Classify rules by project fit only when the user asks for a standards review. If no actionable findings exist, say so explicitly.
