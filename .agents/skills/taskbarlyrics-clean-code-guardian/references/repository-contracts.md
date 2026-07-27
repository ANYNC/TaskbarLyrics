# TaskbarLyrics repository contracts

Read this file for every code/configuration task governed by the skill.

## Architecture and dependency direction

- `TaskbarLyrics.Core` owns platform-neutral lyric retrieval, matching, parsing, routing, caching, local media indexing, persistence, and domain policies.
- `TaskbarLyrics.App` owns WPF/WebView2 hosting, Windows SMTC, WinForms NotifyIcon, audio capture, native windows, startup, and composition.
- Core must not reference WPF, WebView2, WinForms, or Windows-window APIs.
- `AppCompositionRoot` is the cross-domain construction boundary. Windows receive services rather than constructing unrelated infrastructure.
- UI hosts route events and lifecycle. Move business decisions, persistence, coordination, and reusable transformations into focused collaborators.

## WebView contracts

- Settings and lyrics pages are modular HTML/CSS/JavaScript hosted by WebView2.
- Keep the production Web UI framework-free. Do not add React, Vue, Svelte, Tailwind, shadcn/ui runtimes, or a new frontend build pipeline unless the user explicitly approves the architectural change.
- Use shadcn/ui only as a visual and interaction reference. Recreate the intended hierarchy, spacing, colors, radius, shadows, feedback, and motion with semantic HTML, reusable CSS, and modular JavaScript; do not copy React/TSX component implementations.
- Centralize visual tokens and reusable control styles for buttons, fields, dialogs, menus, navigation, and selectors. Avoid repeated inline styles and page-specific copies of the same component behavior.
- Interactive controls must implement applicable hover, pressed, disabled, focus-visible, keyboard, ARIA, validation, Escape, and click-outside behavior. Preserve reduced-motion and high-contrast behavior where relevant.
- C# and JavaScript communicate through V1 envelopes: `{ version: 1, type, payload }`.
- Reject unknown versions, unknown message types, and invalid payloads safely.
- Keep message names and payload ownership centralized; do not reintroduce parallel switch tables across HTML, JavaScript, and C#.
- Preserve `window.settingsApp` and the lyric injection markers `{{STYLE_CSS}}` and `{{APP_JS}}`.
- Load lyric scripts in deterministic order. Update Vitest and .NET protocol tests when messages change.
- Any settings-page marker, navigation, setting, or message change must keep the settings contract test synchronized.

## Settings and persistence

- User settings live at `%APPDATA%\TaskbarLyrics\settings.json`.
- Preserve existing fields and defaults. Read-time migrations must be idempotent; saves write only the current canonical form.
- Use atomic writes for durable state. A read failure must fall back safely without destroying recoverable user data.
- Per-player and per-track offsets must keep their established identity and precedence semantics.
- Never use UI labels as durable status, action, or protocol identifiers.

## Lyrics, media, and caches

- Cache formats and keys are behavior contracts. Version them when parsing, identity, matching, or selection semantics change.
- Prefer stable player song IDs. Do not persist fuzzy metadata matches in a way that can suppress future remote searches.
- Validate cached documents before returning them and remove invalid entries through the cache-store abstraction.
- Local lyrics and cover lookup share `ILocalMediaIndex`; do not add a second directory scan.
- Providers implement boundary interfaces and remain cancellable where their dependencies allow it.
- Preserve provider routing, score thresholds, duration checks, and fallback order unless the task explicitly changes retrieval behavior and adds regression coverage.

## Threading and lifetime

- `LyricsWindowHost` owns a separate STA thread. Access its window only through its dispatcher API.
- Avoid blocking a dispatcher with synchronous waits on asynchronous work.
- Propagate `CancellationToken`; distinguish cancellation from failure.
- Observe background-task exceptions. Make serialization explicit when commands must not overlap.
- Dispose audio capture, native hooks, NotifyIcon, WebView resources, timers, semaphores, and cancellation sources.
- Unsubscribe static/system events and callbacks when their owner closes or is disposed.

## Native windows and DPI

- WPF layout uses device-independent units; Win32 hooks and window rectangles generally use physical pixels. Convert at one named boundary.
- Re-query monitor, work area, taskbar edge, and DPI when showing or repositioning transient windows.
- Native positioning and hit-testing changes require manual checks for resolution changes, scale changes, multiple monitors, negative coordinates, and tray overflow.
- Keep native interop isolated and expose intent through small value objects or adapters.

## Verification and packaging

- `scripts/verify.ps1` runs web tests, App/Core tests, settings contract checks, and format verification.
- `dotnet build TaskbarLyrics.sln` must finish with zero warnings and errors.
- Use `git diff --check` before handoff.
- After successful verification of a runnable-app change, run `scripts/restart-app.ps1` and leave the app ready for immediate user validation. Do not restart for documentation-only, test-only, instruction-only, or build-only changes unless requested.
- Test projects, Vitest, jsdom, and `node_modules` are development-only and must not enter release ZIPs.
- Build outputs, logs, `publish/`, `tmp/`, `bin/`, `obj/`, and `build_verify*/` are not source.
