# Coding standards

House rules for F# in this repo, beyond what tooling already enforces. **Tooling owns formatting** — Fantomas (`fantomas --check`, run in CI) and `.editorconfig` (4-space indent, final newline). Don't restate or hand-fix layout; these rules cover the things a formatter can't see.

Each rule is a convention, not a lint: cite it in review, weigh it against the case. Where a rule and one of Fowler's generic smells disagree, the rule here wins.

## 1. Names and comments use the glossary

`CONTEXT.md` is the ubiquitous language. Identifiers, comments, log strings, and envelope tags that name a domain concept use the glossary term and avoid its listed `_Avoid_` synonyms — **Companion** not "server"/"backend"/"host", **Block** not "request"/"snippet", **Run** not "execute"/"send", **Envelope** not "message"/"payload". "Extension host" is the one sanctioned use of "host" (it's the glossary's own name for the JS side). A name you can't express in glossary terms is a signal the concept is either missing from `CONTEXT.md` or muddled in the code — resolve that, don't reach for a synonym.

## 2. Cross-boundary wire helpers live in one module

Everything that reads or writes the framed envelope wire — frame I/O, `JsonElement` property readers (`getStringProp`, `getIntProp`, `jstr`), and the outcome ↔ wire mapping — belongs in **`Companion.Envelope`** (or is re-exported from there), not copied into each caller. Two copies of a `JsonElement` reader drift (they already disagreed on whether a missing string is `null` or `""`), and the two ends of a channel that serialise the same shape in different modules fall out of sync silently. One module, opened by both ends. `BlockRunner.outcomeToWire`/`wireToOutcome` are the pattern to follow: a single shape, a single inverse, shared by host and worker.

## 3. Every external process gets a bounded wait and a kill path

When you drive a child process (`--worker`, or any `Process.Start`), a crashed child and a *hung* child are different failures and both must terminate the Run:

- A child that closes stdout without a frame → `tryReadFrame` returns `None` → a clean `RuntimeError`. ✓ (already handled)
- A child that emits a frame, or nothing, then **hangs** must not wedge the caller. `proc.WaitForExit()` and a blocking `tryReadFrame` are both unbounded. Use a bounded wait (`WaitForExit(timeoutMs)`) and `proc.Kill()` on expiry, mapped to `RuntimeError`.

`use proc = proc` for disposal is necessary but not sufficient — disposal doesn't unblock a wait. A driven process without a timeout is an incomplete implementation, not a nicety deferred.

## 4. Compound reads-and-writes of process-global state are one atomic step

Process-global mutable state (e.g. `loadedVersions`) guarded by a lock must take that lock **once per logical operation**, not once per access. A check in one `lock` scope followed by an act in another is a TOCTOU gap: correct only while the caller is single-threaded, and it silently isn't the day a second caller appears. If `run` reads `conflictsWithLoaded` then writes `markLoaded`, that check-then-act belongs under one lock. If the state is truly single-threaded and always will be, don't add the lock at all — a half-taken lock advertises a safety it doesn't provide.

## 5. Don't cite issue or PR numbers in source

Source is not the issue tracker. Comments, test names, and identifiers must not carry bare issue/PR numbers (`#38`, `issue #16`, `ticket #17`) — the tracker renumbers and rots, and the number means nothing to someone reading the code. State the *reason* the code exists, not the ticket that asked for it: "fresh session per Run," not "issue #7's fresh-session resolution." Provenance belongs in the commit message and PR, which is exactly where `git blame` sends a reader who wants it.

The one exception is a **`TODO`**, which points at work not yet done — and it carries the **full URL**, never a bare number, so it stays a click away and survives a tracker move:

```fsharp
// TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/42): bound the worker wait
```

In-repo references are fine and encouraged — `ADR-0002`, a file path, another module — because they live and move with the code.

---

*Seeded from a two-axis review. Add a rule only when a real review finding shows an unwritten convention bit — keep this short and concrete, per `docs/agents/domain.md`'s lazy-documentation philosophy.*
