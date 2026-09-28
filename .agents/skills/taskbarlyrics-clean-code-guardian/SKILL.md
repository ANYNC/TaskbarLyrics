---
name: taskbarlyrics-clean-code-guardian
description: Optional TaskbarLyrics review aid for complex refactors and changes involving compatibility, persistence, concurrency, native windows, or multiple modules. Consult relevant project contracts and review dimensions; routine edits do not require this skill.
---

# TaskbarLyrics Clean Code Guardian

Apply Clean Code as a behavior-preserving engineering discipline. Optimize for correctness, clarity, cohesion, explicit boundaries, testability, and safe evolution—not arbitrary class or method counts.

## Consult the relevant project rules

Use these references when they apply to the change; do not require a full read for every task:

- `references/repository-contracts.md` for TaskbarLyrics architecture and compatibility boundaries.
- `references/clean-code-rubric.md` for review dimensions relevant to the change.

Root `AGENTS.md` is the entry-point policy. The references provide detailed context, not additional gates for unrelated work.

## Load the WebView UI standard when applicable

For WebView interface work, consult the relevant sections of `../../../docs/WebView界面视觉与交互规范.md`. Existing tokens and patterns are a baseline that can be improved with a documented reason. External guidelines are supplementary.

## Classify the request

- For a review or diagnosis only, inspect and report evidence. If the user also requests a fix, implement it.
- For implementation or refactoring, preserve the current behavior contract, make the smallest complete change, and verify its effects.
- Treat changes to stored settings, WebView messages, public behavior, release packaging, or user data as explicit scope requiring compatibility analysis and appropriate tests.

## Establish the change contract

Before editing:

1. Inspect `git status` and separate pre-existing changes from task changes.
2. Locate the owning component and the callers, tests, persistence formats, or UI/protocol consumers relevant to the change.
3. State the behavior that must remain unchanged and the failure being corrected or capability being added.
4. Identify relevant thread, lifetime, cache, compatibility, native-coordinate, or packaging risks.
5. Prefer an existing seam. Add an abstraction only when it removes a real dependency, duplication, or testing barrier.

## Implement cleanly

- Keep policy in Core and Windows/UI mechanisms in App.
- Keep windows and WebView handlers thin; delegate domain behavior to focused services.
- Maintain one source of truth for settings keys, protocol types, hotkey actions, cache versions, and status codes.
- Use names that expose intent and units. Avoid boolean blindness, temporal coupling, hidden global state, and unrelated parameters.
- Keep functions at one abstraction level with explicit guard clauses and failure behavior.
- Propagate cancellation and make ownership/disposal visible. Never introduce unobserved fire-and-forget work.
- Validate data at boundaries. Preserve atomic persistence and safe fallback behavior.
- Do not retain dead compatibility branches after callers have migrated unless a documented external contract requires them.
- Avoid analyzer suppressions, catch-all exception swallowing, test-only production hooks, and comments that merely restate code.

## Prove the change

Run verification proportional to impact:

1. Add or update a regression test for observable behavior and relevant failure paths when practical.
2. Run targeted or affected-area tests while iterating. Run full verification for broad cross-module, release, or compatibility-sensitive changes.
3. For settings changes, run relevant `tests/web/bridge.test.js` and App tests covering persistence or message parsing.
4. Run a zero-warning solution build for production C# integration or release work, and `git diff --check` before handoff.
5. Record only manual checks relevant to behavior that automated tests cannot cover reliably.
6. Update the engineering change record when the root rules require it.
7. Restart the app only when a real-app check is needed or the user asks for it ready to validate.

Do not claim completion when required verification was skipped or failed. Report the exact missing evidence.

## Self-review before handoff

Review the final diff against relevant dimensions in `references/clean-code-rubric.md`. Check especially for:

- accidental behavior or persistence changes;
- new duplication or competing sources of truth;
- dependency-direction violations;
- missing cancellation, disposal, or event unsubscription;
- unsafe cache reuse or migration;
- UI/protocol drift across C# and JavaScript;
- tests that assert implementation details instead of observable behavior;
- unrelated formatting or cleanup that obscures the change.

Report the outcome first, then changed behavior, verification evidence, and any remaining manual checks or risks. Keep unrelated user changes out of the summary.
