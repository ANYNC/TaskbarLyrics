# Clean Code review rubric

Read this file for every code/configuration task governed by the skill. Review all dimensions; report only findings that materially improve correctness, clarity, safety, testability, or maintainability.

## 1. Behavior and correctness

- Does the change implement the requested behavior without unintended visible changes?
- Are boundary cases, invalid input, partial failures, retries, and recovery explicit?
- Are compatibility assumptions backed by tests or documented manual verification?

## 2. Names and readability

- Do names reveal intent, domain meaning, units, ownership, and lifecycle?
- Remove misleading abbreviations, generic names, double negatives, and comments needed only because names are vague.
- Prefer code that reads in execution order without requiring hidden context.

## 3. Functions and control flow

- Keep one abstraction level per function and one reason for it to change.
- Prefer guard clauses over deep nesting and extract decisions that need names or independent tests.
- Avoid flag parameters, long parameter lists, hidden mutation, temporal coupling, and catch-all branches.

## 4. Classes, cohesion, and responsibility

- Keep state beside the behavior that owns it.
- Split classes when unrelated actors force changes, not merely because a line-count threshold is exceeded.
- Prevent windows, providers, stores, and coordinators from becoming service locators or grab bags.

## 5. Architecture and dependency direction

- Dependencies point toward domain policy; platform mechanisms stay at the edge.
- Depend on interfaces where multiple implementations, testing seams, or boundary isolation justify them.
- Avoid speculative layers, pass-through wrappers, cyclic dependencies, and cross-domain object construction.

## 6. Encapsulation and interfaces

- Expose the smallest capability required by callers.
- Use domain value objects or DTOs at boundaries; prevent primitive strings and booleans from carrying multiple meanings.
- Keep invariants inside the owning type and make invalid states difficult to represent.

## 7. Duplication and sources of truth

- Centralize identifiers, defaults, mappings, protocol types, cache versions, settings keys, and action catalogs.
- Distinguish true knowledge duplication from superficially similar code with different reasons to change.
- Remove migrated legacy paths once compatibility no longer requires them.

## 8. Errors and observability

- Catch exceptions only where the code can add context, recover, translate, or preserve a boundary.
- Never silently swallow failures or log the same error repeatedly at multiple layers.
- Logs must identify operation and safe diagnostic context without leaking sensitive data or flooding polling loops.

## 9. Async, cancellation, and concurrency

- Propagate cancellation through async boundaries and preserve cancellation semantics.
- Avoid sync-over-async, unobserved tasks, overlapping polls, races, and locks held across awaits.
- Make thread affinity, serialization, timeout ownership, and dispatcher transitions explicit.

## 10. Resources and lifecycle

- Every disposable resource, event subscription, timer, hook, and background operation has a clear owner.
- Disposal is idempotent and follows dependency lifetime order.
- Closing a window or service cannot leave callbacks targeting disposed state.

## 11. State, caches, and persistence

- State transitions are explicit and atomic where required.
- Cache identity, validity, expiry/versioning, fallback, and cleanup are defined.
- Persistence migrations are backward-compatible, idempotent, and covered by representative old data.

## 12. Testability and tests

- Separate pure policy from I/O, time, static APIs, UI dispatchers, and native calls.
- Tests assert observable behavior, contracts, and failure paths—not private implementation shape.
- A regression fix includes a test that would have failed before the fix whenever practical.

## 13. Performance and efficiency

- Avoid repeated directory scans, repeated parsing, unnecessary allocations, UI-thread blocking, and unbounded task creation.
- Optimize measured or structurally obvious costs; do not trade clarity for speculative micro-optimization.
- Analyzer performance suggestions must not weaken public abstractions or domain clarity.

## 14. Security and boundary validation

- Treat WebView messages, JSON, file paths, native handles, and external API responses as untrusted boundary data.
- Validate type, range, version, path scope, and nullability before mutation or I/O.
- Do not introduce command injection, path traversal, secret leakage, or unsafe destructive operations.

## 15. Comments, documentation, and dead code

- Comments explain constraints, compatibility, thread rules, or non-obvious decisions—not syntax.
- Update documentation when user behavior, configuration, verification, or architecture changes.
- When an engineering change record is required, keep the newest completed entry first, preserve descending dates, and keep the reusable template last.
- Remove unused code and stale historical comments only when references and compatibility have been checked.

## 16. Change quality

- Keep the diff focused and reversible; separate mechanical formatting from behavioral changes when practical.
- Preserve unrelated user work and avoid opportunistic redesign outside the task.
- Completion requires verification evidence and an honest statement of remaining manual checks or risks.
