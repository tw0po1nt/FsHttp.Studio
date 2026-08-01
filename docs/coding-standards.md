# Coding standards

House rules for F# in this repo, beyond the rules that tooling already enforces. **Tooling owns formatting.** Fantomas (`fantomas --check`, run in CI) and `.editorconfig` (4-space indent, final newline) do that work. Do not restate the layout rules, and do not fix layout by hand. These rules cover what a formatter cannot see.

Each rule is a convention, not a lint. Cite it in review, and weigh it against the case. Where a rule here and one of Fowler's generic smells disagree, the rule here wins.

## 1. Names and comments use the glossary

`CONTEXT.md` is the ubiquitous language. Identifiers, comments, log strings, and envelope tags that name a domain concept use the glossary term, and avoid the listed `_Avoid_` synonyms. Write **Companion**, not "server", "backend", or "host". Write **Block**, not "request" or "snippet". Write **Run**, not "execute" or "send". Write **Envelope**, not "message" or "payload". "Extension host" is the one sanctioned use of "host", because it is the glossary's own name for the JS side. A name that you cannot express in glossary terms is a signal: the concept is either missing from `CONTEXT.md` or muddled in the code. Resolve that, and do not reach for a synonym.

## 2. Cross-boundary wire helpers live in one module

Everything that reads or writes the framed envelope wire belongs in **`Companion.Envelope`**, or is re-exported from there. Do not copy it into each caller. This covers frame I/O, the `JsonElement` property readers (`getStringProp`, `getIntProp`, `jsonString`), and the outcome-to-wire mapping.

Two copies of a `JsonElement` reader drift apart. They already disagreed on whether a missing string is `null` or `""`. Two ends of a channel that serialize the same shape in different modules also fall out of step silently. Use one module, and open it at both ends. `BlockRunner.outcomeToWire` and `wireToOutcome` are the pattern to follow: one shape, one inverse, shared by the host and the worker.

## 3. Every external process gets a bounded wait and a kill path

When you drive a child process (`--worker`, or any `Process.Start`), a crashed child and a *hung* child are different failures. Both must terminate the Run:

- A child that closes stdout without a frame → `tryReadFrame` returns `None` → a clean `RuntimeError`. ✓ (already handled)
- A child that emits a frame, or nothing, and then **hangs** must not block the caller. `proc.WaitForExit()` and a blocking `tryReadFrame` are both unbounded. Use a bounded wait (`WaitForExit(timeoutMs)`), call `proc.Kill()` on expiry, and map the result to `RuntimeError`.

`use proc = proc` gives disposal, which is necessary but not sufficient. Disposal does not unblock a wait. A driven process without a timeout is an incomplete implementation, not a deferred nicety.

## 4. Compound reads-and-writes of process-global state are one atomic step

Process-global mutable state under a lock, such as `loadedVersions`, must take that lock **once for each logical operation**, not once for each access. A check in one `lock` scope, followed by an act in another scope, is a TOCTOU gap. It is correct only while the caller is single-threaded, and it stops being correct silently on the day a second caller appears. If `run` reads `conflictsWithLoaded` and then writes `markLoaded`, that check and that act belong under one lock. If the state is single-threaded and always will be, do not add the lock at all. A half-taken lock advertises a safety that it does not provide.

## 5. Do not cite issue or PR numbers in source

Source is not the issue tracker. Comments, test names, and identifiers must not carry bare issue or PR numbers, such as `#38`, `issue #16`, or `ticket #17`. The tracker renumbers its items, and the number tells a reader of the code nothing. State the *reason* that the code exists, not the ticket that asked for it. Write "fresh session per Run", not "issue #7's fresh-session resolution". Provenance belongs in the commit message and the PR, which is where `git blame` sends a reader who wants it.

A **`TODO`** is the one exception, because it points at work that is not yet done. A `TODO` carries the **full URL**, never a bare number, so it stays one click away and survives a move of the tracker:

```fsharp
// TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/42): bound the worker wait
```

In-repo references are correct and encouraged, such as `ADR-0002`, a file path, or another module. They live and move with the code.

---

*Seeded from a two-axis review. Add a rule only when a real review finding shows that an unwritten convention caused a problem. Keep this file short and concrete, which follows the lazy-documentation philosophy in `docs/agents/domain.md`.*
