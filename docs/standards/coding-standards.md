# Coding standards

House rules for F# in this repo, beyond the rules that tooling already enforces. **Tooling owns formatting.** Fantomas (`fantomas --check`, run in CI) and `.editorconfig` (4-space indent, final newline) do that work. Do not restate the layout rules, and do not fix layout by hand. These rules cover what a formatter cannot see.

Each rule is a convention. Cite it in review, and weigh it against the case. Where a rule here and one of Fowler's generic smells disagree, the rule here wins.

## 1. Names and comments use the glossary

`GLOSSARY.md` is the ubiquitous language. Identifiers, comments, log strings, and envelope tags that name a domain concept use the glossary term, and avoid the listed `_Avoid_` synonyms. Write **Companion** in place of "server", "backend", or "host". Write **Block** in place of "request" or "snippet". Write **Run** in place of "execute" or "send". Write **Envelope** in place of "message" or "payload". "Extension host" is the one sanctioned use of "host", because it is the glossary's own name for the JS side. A name that you cannot express in glossary terms is a signal: the concept is either missing from `GLOSSARY.md` or muddled in the code. Resolve that, and do not reach for a synonym.

## 2. Cross-boundary wire helpers live in one module

Everything that reads or writes the framed envelope wire belongs in **`Companion.Envelope`**, or is re-exported from there. Do not copy it into each caller. This covers frame I/O, the `JsonElement` property readers (`getStringProp`, `getIntProp`, `jsonString`), and the outcome-to-wire mapping.

Two copies of a `JsonElement` reader drift apart. They already disagreed on whether a missing string is `null` or `""`. Two ends of a channel that serialize the same shape in different modules also fall out of step silently. Use one module, and open it at both ends. `BlockRunner.outcomeToWire` and `wireToOutcome` are the pattern to follow: one shape, one inverse, shared by the host and the worker.

## 3. Every external process gets a bounded wait and a kill path

When you drive a child process (`--worker`, or any `Process.Start`), a crashed child and a *hung* child are different failures. Both must terminate the Run:

- A child that closes stdout without a frame → `tryReadFrame` returns `None` → a clean `RuntimeError`. ✓ (already handled)
- A child that emits a frame, or nothing, and then **hangs** must not block the caller. `proc.WaitForExit()` and a blocking `tryReadFrame` are both unbounded. Use a bounded wait (`WaitForExit(timeoutMs)`), call `proc.Kill()` on expiry, and map the result to `RuntimeError`.

`use proc = proc` gives disposal, which is necessary but not sufficient. Disposal does not unblock a wait. A driven process without a timeout is an incomplete implementation.

## 4. Compound reads-and-writes of process-global state are one atomic step

Process-global mutable state under a lock, such as `loadedVersions`, must take that lock **once for each logical operation**. A check in one `lock` scope, followed by an act in another scope, is a TOCTOU gap. It is correct only while the caller is single-threaded, and it stops being correct silently on the day a second caller appears. If `run` reads `conflictsWithLoaded` and then writes `markLoaded`, that check and that act belong under one lock. If the state is single-threaded and always will be, do not add the lock at all. A half-taken lock advertises a safety that it does not provide.

## 5. Comments state what the code cannot, and they speak from where they stand

Write a comment only for what a competent reader cannot derive from the code itself: an external
constraint, a non-obvious invariant, or a workaround for a defect elsewhere. Keep it to one line,
and state the fact. That list is the whole permission. A comment that describes what the code does,
or that argues for your implementation choice, falls outside it, and so does a new comment on
existing code. Explain a decision you made in your reply to the user, where the commit message and
the pull request keep it.

A comment speaks from where it stands. It cites nothing that sends the reader off the page: no
spec path, no ADR number, no file path, no URL, and no issue or PR number such as `#38` or
`ticket #17`. A citation makes the reader open a second document to learn what the line in front of
them means, and the reader pays that cost on every pass. State the constraint itself, because the
constraint is the thing the reader needs. A tracker number carries the further defect that the
tracker renumbers its items. `git blame`, the commit message, and the pull request keep the history,
and a test name states the behavior under test rather than the ticket that asked for it.

A **`TODO`** is the one exception, because it points at work that does not exist yet, so no fact on
the page can stand for it. A `TODO` carries the **full URL**, so the work stays one click away and
survives a move of the tracker:

```fsharp
// TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/42): bound the worker wait
```

This rule governs `///` XML doc comments as much as `//` comments. A doc comment that restates the
signature, such as `/// Gets the name` above `member Name`, adds no information. A reader must read
past it for nothing. Write a `///` comment only when it states something the signature does not,
such as a parameter's unit or valid range, a non-obvious exception, or a constraint on how to use
the member.

This rule is an overlay on the vendored `simplified-technical-english` skill, whose "Code comments
and software text" section says that a comment explains why the code exists. Apply STE to the
wording of a comment that this rule permits, and take the question of whether to write the comment
from here.

## 6. A record of closures needs strong justification

A record whose fields are function types is a smell. It can hide a cycle between two modules. It can also hide state that a caller cannot see or test without a call to the closure. Consider these alternatives, in this order, before you use one:

1. **Reorder the code to break the cycle.** A closure-record field often exists only to let module A call module B before B is fully defined. Move the shared code earlier, or split it into a third module that both modules open. The closure is then not necessary.
2. **For a narrow scope, use individual function parameters.** A function that needs a callback takes that callback as a parameter. The dependency stays visible at the call site. A test can supply the callback directly, with no record to construct first.
3. **To share state across a boundary, use an interface.** An interface names its members. A caller can see what the interface does without a read of a closure body. A test can supply a fake implementation, with no real state to set up.

Use a closure-record field only when none of these three alternatives fits. State the reason in your reply to the user, and keep it out of a source comment (rule 5).

## The hook is a backstop

A `PreToolUse` hook (`.claude/settings.json`) fires before a `Write` or an `Edit` on a `.fs` or
`.fsx` file. It names the rules above at the moment an agent is about to write F#. The hook cannot
read the pending code, and it cannot judge a comment. It also cannot see a file that a Bash command
writes. The rules above are still the requirement.

---

*Seeded from a two-axis review. Add a rule only when a real review finding shows that an unwritten convention caused a problem. Keep this file short and concrete, which follows the lazy-documentation philosophy in `docs/agents/domain.md`.*
