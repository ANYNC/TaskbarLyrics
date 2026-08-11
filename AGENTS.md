# TaskbarLyrics repository instructions

Read this file before changing the repository. Keep user-visible behavior and stored user data stable unless the user explicitly approves a migration.

## Mandatory Clean Code workflow

- For every code/configuration plan, implementation, bug fix, refactor, code review, or verification task, read `.agents/skills/taskbarlyrics-clean-code-guardian/SKILL.md` completely before acting.
- Read every reference that skill marks as required. This applies even when the skill is not shown in the active skill catalog.
- Documentation-only copy edits do not require the skill unless they change development policy, architecture, behavior contracts, or verification instructions.

## Build and verification

- Requires .NET 8 SDK, Windows x64, and Windows 11 SDK 10.0.22621.
- Run: `dotnet run --project TaskbarLyrics.App`
- Build: `dotnet build TaskbarLyrics.sln`
- Targeted verification while iterating: `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Tier Targeted -Area Core -Filter FullyQualifiedName~TestClassName`
- Affected-project verification: `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Tier Project -Area Core` (areas: `Core`, `App`, `Web`, `Settings`; multiple areas are allowed)
- Full delivery verification: `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1`
- Restart local app: `powershell -ExecutionPolicy Bypass -File scripts/restart-app.ps1`
- Release packaging: `powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1`
- Before handoff, also run `git diff --check`. For production code changes, require a zero-warning solution build.
- After verification succeeds for a change that affects the runnable app, run `scripts/restart-app.ps1` and leave the app ready for user validation. Skip this for documentation-only, test-only, instruction-only, or build-only changes, or when the user opts out.

The verification script defaults to `Full`, which runs Vitest/jsdom web tests, App tests, Core tests, the settings contract test, and `dotnet format --verify-no-changes`. Use `Targeted` for the directly affected test class or web test file while iterating, `Project` after the affected feature is complete, and `Full` only at the delivery boundary. Tests change when an observable behavior or compatibility contract changes; implementation-only refactors should preserve existing tests whenever practical.

## Solution layout

- `TaskbarLyrics.Core`: platform-neutral lyric retrieval, matching, parsing, caching, local media indexing, persistence, and policies. Do not add WPF, WebView2, WinForms, or native-window dependencies here.
- `TaskbarLyrics.App`: Windows host using WPF, WinForms NotifyIcon, WebView2, SMTC, audio capture, native-window integration, and the composition root.
- `TaskbarLyrics.Core.Tests` and `TaskbarLyrics.App.Tests`: xUnit regression tests.
- `tests/web`: Vitest/jsdom behavior tests.
- `TaskbarLyrics.App/Web`: modular lyrics and settings interfaces hosted in WebView2.

## Behavior contracts

- Preserve existing `settings.json` fields and migration behavior unless a migration is explicitly requested and tested.
- Preserve the WebView V1 envelope `{ version: 1, type, payload }`; unknown versions, types, and invalid payloads must fail safely.
- Keep the production Web UI framework-free: use native HTML, CSS, and JavaScript. Treat shadcn/ui as a design reference, not a runtime or source dependency.
- Keep `{{STYLE_CSS}}` and `{{APP_JS}}` injection markers and deterministic lyric-script ordering.
- Changes to settings HTML/JS/CSS/C# must update and pass the settings contract test.
- Cache formats must be versioned. Never allow stale or fuzzy matches to become durable without a stable media identity.
- Keep windows as UI hosts. Cross-domain construction belongs in `AppCompositionRoot`.

## Windows and concurrency boundaries

- `LyricsWindowHost` owns a separate STA thread. Access its window only through its dispatcher boundary.
- Propagate cancellation, observe asynchronous failures, dispose native/audio/WebView resources, and unsubscribe events.
- Do not mix WPF device-independent units with Win32 physical pixels. Native positioning changes must consider DPI changes, resolution changes, taskbar edge, and multiple monitors.

## Independent technical judgment

- Treat the user's goal as authoritative, but treat a proposed implementation as a hypothesis that must be evaluated.
- Do not agree with or implement a proposal merely because the user suggested it.
- When a proposal conflicts with repository contracts, adds unnecessary complexity, weakens correctness, compatibility, testability, maintainability, or user experience, or when a materially better solution exists, state the concern clearly before acting.
- Support objections with concrete evidence, likely consequences, and a recommended alternative. Distinguish objective defects from subjective preferences.
- Preserve the user's underlying goal when recommending another approach; challenge the implementation, not the user.
- Do not manufacture objections over minor stylistic differences. Raise concerns only when they materially affect the project.
- When multiple approaches are valid, explain the relevant trade-offs and make a clear recommendation instead of giving unqualified agreement.
- If the user knowingly accepts a reversible trade-off that does not violate safety or repository contracts, follow that decision and report the remaining risk honestly.

## Sol + Luna development mode

- Use Sol as the primary agent for requirement interpretation, technical and architectural decisions, task decomposition, cross-module coordination, risk trade-offs, final integration, and delivery acceptance.
- Use the project-scoped `luna_worker` as the primary coding executor for clearly bounded production-code and test-code work. This includes implementing decided behavior, fixing located defects, performing local behavior-preserving refactors, adding or updating directly related tests, and running targeted verification.
- Delegate automatically only when the task has a clear objective, file or module ownership, preserved behavior, acceptance criteria, and verification target, and when the expected execution work is large enough to justify the extra agent startup, context transfer, and result-integration cost.
- Do not delegate trivial one-step edits, work Sol can complete directly with less total effort, final review, tightly coupled work that needs continuous shared context, or open-ended product, architecture, protocol, persistence, migration, or security decisions.
- Before spawning `luna_worker`, Sol must provide a self-contained minimal delegation packet containing the objective, allowed ownership boundary, already-decided implementation constraints, behavior that must remain stable, prohibited scope, targeted verification, known workspace changes, and relevant evidence. Do not paste the full `AGENTS.md`, skill instructions, or unrelated conversation history into the packet.
- Use the smallest context fork that still makes the delegation packet complete. Treat automatically loaded `AGENTS.md` guidance as already available; do not reopen it merely to restate it. Mandatory skill and reference reads still apply exactly as their higher-priority instructions require.
- Prefer one bounded deliverable per worker. Run multiple workers in parallel only when their tasks are independent and their write ownership does not overlap; never assign multiple write agents to the same files or a highly coupled behavior path.
- `luna_worker` must not spawn additional subagents unless Sol explicitly delegates that authority. It may make reversible local implementation choices inside its boundary, but must return behavior, architecture, compatibility, persistence, security, ownership, and scope-expansion decisions to Sol with evidence and a recommendation.
- Sol should not repeat implementation-level exploration or targeted checks already completed by Luna when the handoff includes current, sufficient evidence. Sol must still inspect the actual diff, resolve escalated decisions, run required integration and delivery verification, update the engineering change record when required, restart the runnable app when required, and own the final user-facing answer.
- If a task is not suitable for delegation, Sol completes it directly. Optimize for total verified outcome and protected primary-agent context, not for maximizing the number of subagents.

## Change discipline

- Inspect `git status` before editing and preserve unrelated user changes.
- Diagnose requests are read-only unless the user asks for a fix.
- Prefer the smallest complete behavior-preserving change; do not add speculative abstractions.
- Do not suppress analyzer warnings to hide design problems. Document any unavoidable suppression next to the boundary it protects.
- Add regression tests for changed logic and failure paths. Record manual checks for Windows behavior that cannot be automated reliably.
- Update `docs/工程变更记录.md` once when a complete, independently verifiable feature, fix, migration, refactoring stage, or important engineering-policy change is completed and verified. Insert the new entry immediately after the document's record rules; keep entries in descending date order and, within the same date, newest-completed first; keep the entry template permanently at the end and never append a new entry after older records. Record user impact, technical boundaries, compatibility, verification, and remaining work; do not log conversation turns, abandoned attempts, file-by-file edits, formatting, or trivial renames.
- Do not commit, push, publish, delete user data, or change external state unless the user authorizes it.

## Generated and local data

- Ignore `publish/`, `tmp/`, `bin/`, `obj/`, `build_verify*/`, `node_modules/`, logs, and runtime data under `%APPDATA%\TaskbarLyrics`.
- Test projects and frontend development dependencies are not included in the release ZIP.
