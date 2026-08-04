# Reach a block anywhere in a script: the Setup boundary and the two AST routes

Spec for #90 §1.

This is v0.2's headline change. It replaces how a Run reaches the block that the user clicked.

## Problem Statement

A user clicks `▶ Run request` on a block. FsHttp.Studio builds the Setup by **text slicing**.
It takes every line before the block. It blanks the enclosing statement of every other block.
It then evaluates the block's bare `http { }` text alone, piped to `Request.send`.

This works only when the block is a bare expression at the top level of the script. In each other
position the Run fails, and the message names the wrong problem:

- The block is the right side of a `let`. The slice keeps the `let` head and removes its value,
  so the Setup does not compile.
- The block is in a `module`. The slice keeps `module M =` with no body, and the names in the
  module are not in scope for the block.
- The block is in a loop body or a branch. The slice keeps `for x in xs do` with no body.

SchlenkR reported this in #90 §1, with a 12-case test file. Cases 4 to 10 fail today. The usual
message is *"The value or constructor 'http' is not defined"*, which names the one identifier that
is correct.

The user reads the message as a defect in their own script. The defect is in the slice.

## Solution

A Run reaches the block **where the user wrote it**. It does not extract the block.

The Run evaluates the user's script from the first line to the end of the target block's own
expression, and no further. It keeps every enclosing `module`, `let`, and type header intact. It
then invokes the block by name and sends it.

The Run gets the name in one of two ways:

- **R1** — the block is a bare expression statement. The Run inserts `let <name> = ` immediately
  before the block, on the same line, and then invokes `<name>`.
- **R2** — the block is the value of a module-level binding. The Run invokes the binding's own
  name.

A position that neither route reaches is **refused**. The Run does not attempt it, and does not
guess. The Run decides each refusal from the untyped syntax tree, with no type-check.

This makes 10 of the 13 positions in #90's matrix run correctly, and each of the other three
refuse with a reason. The generalized set is 22 positions: 18 supported, 4 refused.

## User Stories

1. As a script author, I want a Run on a `let`-bound block to send that request, so that I do not
   have to move the block to the top level.
2. As a script author, I want a Run on a block in a `module` to send that request, so that I can
   group my requests.
3. As a script author, I want a block that uses a `let` from its own module to run, so that shared
   values work where I wrote them.
4. As a script author, I want a Run on a `()`-callable function's block to send that request, so
   that a helper function is a usable place for a block.
5. As a script author, I want a Run on a `private` binding to send that request, so that
   visibility does not change what I can run.
6. As a script author, I want each Run to send only one request, so that a click does not fire
   a request that I did not ask for.
7. As a script author, I want the code after my block to stay unevaluated, so that a Run does not
   print, write a file, or send a request that follows the block.
8. As a script author, I want a block that the script pipes to `Request.send` to send one request,
   not two, so that the behavior that works today keeps working.
9. As a script author with two blocks on one line, I want a Run on either one to work, so that
   FsHttp.Studio does not delete the block that I clicked.
10. As a script author with a block in a class member, I want a Run on a different block to work,
    so that one unrunnable position does not break the other positions.
11. As a script author, I want a compile error in my block to keep its own message and position,
    so that I can correct my block.
12. As a script author, I want a compile error in my Setup to stay distinct from an error in my
    block, so that I know which part failed.
13. As a script author, I want a response body to stay readable, so that the reach mechanism does
    not undo the response-reading guard.
14. As a script author with a block that FsHttp.Studio cannot run, I want a message that names the
    position, so that I do not read a message about `http`.
15. As a maintainer, I want the routing decision to come from the untyped syntax tree, so that the
    lens work can use the same decision without a type-check.
16. As a maintainer, I want the setup boundary recorded as a decision, so that the session-model
    work knows what promise it renegotiates.

## Implementation Decisions

### 1. The setup boundary: everything above the block, and the block, and nothing after it

A Run evaluates the script from line 1 to the **end of the target block's own expression**. The
Run keeps every enclosing header, and truncates the last line at the block's end column.

This is the most consequential decision in this spec. We considered two other boundaries and
rejected them:

- **The whole file.** Every blanked statement below the target must then compile, and each side
  effect after the target then runs. A script that prints, writes a file, or sends a request after
  the block behaves differently. #90 does not ask for this.
- **The end of the enclosing top-level statement.** A `module` is one statement. A side effect
  after the block, but inside the same module, runs.

The block's own expression end closes both, at each nesting depth.

The boundary also removes a special case. Matrix case 12 is `http { } |> Request.send`. The
`|> Request.send` text is after the block's own range, so the boundary drops it. The Run sends one
request, and no routing branch is necessary. The same rule drops a trailing pipe from a `let`
binding, so `let a = http { } |> Request.send |> ignore` binds the block itself.

### 2. Two routes, and a decidable refusal for each other position

Classify the target from the untyped syntax tree path. Do not type-check. Do not load a project.
Do not resolve NuGet packages.

**R1 — a bare expression statement, at any module depth.** Insert `let <name> = ` at the block's
own start column, on the block's own line. Invoke the name, qualified by the enclosing nested
modules, outermost first.

**R2 — the value of a module-level binding, at unit arity.** Invoke the binding's derived name.
`let pikachu = …` invokes `pikachu`. `let getSnorlax () = …` invokes `getSnorlax ()`. The binding's
value must be the block after the truncation in Decision 1. Only a type annotation or parentheses
can be between the binding and the block.

Consume an ancestor expression that **starts at the same position as the block** before you
classify. That ancestor is a suffix that the boundary drops. This is why case 12 needs
no branch. Do not consume a tuple expression. Its first element starts at the block and its
second does not, and that asymmetry gives one shape two verdicts.

The Run refuses each other position. The refusal families are internal routing tags:

| Family | Meaning | Positions |
|---|---|---|
| F1 | The position is decided at run time | loop body, `if` branch, `match` clause, `try`/`with` handler |
| F2 | We would have to invent a value | a function with arguments, a class member |
| F3 | The position is not module-scoped | an inner `let`, a lambda-valued binding |
| F5 | The binding's value is not the block | a tuple binding, a block inside another block's expression |

F4 is **not** a lens-time or classify-time family. Refer to Decision 8.

The refusal wording, the lens titles, and the wire codes belong to the refusal-lens spec. This spec
produces the verdict only.

### 3. `LocatedBlock` carries the route and the spans

Keep the name `LocatedBlock`. It is the glossary's word. Extend it:

| Field | Purpose |
|---|---|
| `Block` | The `http { }` range, as today |
| `Route` | `R1`, `R2 of invocation`, or `Refused of family * reason` |
| `Blank` | The span to blank when this block is **not** the target. Replaces `Statement` |
| `Qualifier` | The enclosing nested-module names, outermost first |
| `PrivateSpans` | The `private` keywords to blank on the target's own path |
| `TypeAnnotation` | The target binding's own type annotation, with its colon. The R2 route only. Refer to Decision 7 |

Put the classification in `BlockLocator`, beside the location that it reads. Put the text building
in `BlockRunner`. The refusal-lens spec needs the verdict without a Run, and this seam gives it
one.

Remove `enclosingStatementRange` and the `Statement` field. Refer to Decision 5 for what
replaces them. `BlockLocator.locate` and the `blocks` envelope do not change in this spec.

The reference implementation is `RunPlan.fs` in the prototype. Refer to Further Notes.

### 4. Blank every other block, whether or not it sends

Blank the whole statement of each **other** block, and not only the blocks that send. Blanking does
two jobs:

- **Isolation.** One Run sends one request.
- **Value leakage.** A block that stays intact binds a value. `let dexId = http { }` then binds a
  `HeaderContext`, and a later block that uses `dexId` sends a request to a URL that contains
  `FsHttp.DslCE+HeaderContext`. Statement-level blanking removes the binding, so the Run fails with
  a clean *"The value or constructor 'dexId' is not defined"*.

Do not change to expression-level blanking. The clean message is what Decision 8 recognizes.

Keep the current blanking method: replace the span with `()`, and pad it with spaces to the
original width. Each line and each untouched column keeps its position.

### 5. The two blanking hazards

**Hazard 1 — a sibling whose span contains the target.** `let a, b = http { }, http { }` gives both
blocks one statement span. Today's filter compares block ranges, so a Run on `b` blanks the
statement that holds `b`, and deletes the block that the user clicked. **Never blank a span that
contains the target.** A sibling that stays intact is safe, because it does not send.

**Hazard 2 — the blank span is too large.** Today the span is the nearest `SynModule` declaration.
For a block in a class member, that declaration is the **whole type definition**. A Run on a
different block then deletes the type. The code above the target that names that type stops
compiling.

Blank the smallest enclosing statement that can hold an expression. This needs one split:

- A **module-level** binding blanks its whole declaration. This keeps the value-leakage protection
  in Decision 4.
- A **member or inner** binding blanks its right side only. A blanked `member _.Get() = …` leaves a
  type with no members, and that does not parse.

The Run refuses a class-member block as a target, and it can break another block's Run as a
sibling. Both hazards are in scope for v0.2.

### 6. Blank `private` to spaces. `internal` needs nothing

Each invocation is a separate FSI interaction, so a `private` binding is not accessible from it.
The failure is the confusing *"not accessible from this code location"*.

Blank the `private` keyword to spaces on the target's own path: its own binding, and each enclosing
module. Blanking to spaces keeps each line and each column, so no offset arithmetic follows.

Two traps:

- **`private` is not always on the binding.** For `let private x = …` it is on the head pattern
  (`SynPat.Named`), and not on `SynBinding.accessibility`. Read both. A read of the binding alone
  misses it silently.
- **Locate the keyword. Do not assume its position.** An attributed binding's statement range starts
  at its attribute line.

`module private Vault` carries the keyword on the module's own information, and it works the same
way.

`internal` needs no treatment. We measured it: an `internal` binding is accessible from a later
interaction. Do not blank it.

### 7. Blank the target binding's own type annotation (R2 only)

The truncation in Decision 1 drops a trailing pipe. A type annotation that describes the untruncated
value then contradicts the truncated value. `let x : Response = http { } |> Request.send` becomes
`let x : Response = http { }`, which does not compile, and the message blames a correct annotation.

Blank the target binding's own type annotation to spaces, with the method in Decision 6. Blank it on
the R2 route only, and only for the target. Keep the bound name.

This rule is new in this spec. The prototype did not cover it. It costs one span and it removes a
false compile error against correct user code. Refer to the test case in Testing Decisions.

### 8. The block's text is now inside the Setup interaction, so split the diagnostics

This is the change with the largest effect on the code that reports errors.

Today the Run evaluates the Setup, and then evaluates the block's text as a **second** interaction.
This construction keeps the two failures distinct. With this change, the block's text is part of the
Setup interaction, and the invocation is the second interaction. Without a rule, the Run reports
each compile error in the user's block as a Setup failure.

**The rule: a diagnostic that starts inside the target block's span is a block diagnostic. Each
other error diagnostic is a Setup diagnostic.** Compute the block's span in Setup coordinates.
Those coordinates are the block's range with the R1 column shift applied. Refer to Decision 9.

- A **block** diagnostic keeps the compiler's text without changes, at its own position.
- A **Setup** diagnostic keeps the introductory sentence and the clamp rule that the swallowed-Setup
  spec (#95) defines.

This amends #95's Decision 3, which applies the Setup wording to each diagnostic of the Setup
interaction. That was correct when the block was not in the Setup. Apply the Setup wording to the
diagnostics that this rule leaves.

Remove `targetDiagnostic`. There is no separate block interaction to map from. The invocation
interaction produces the block's value or an exception, and its diagnostics have no user-source
position. Report the invocation's own error diagnostics with the Setup treatment, because a failure
there is a failure of our own generated text.

Both tests that exist today must keep passing: *"a non-compiling target block returns compileError
with a source range"* and *"a non-compiling setup returns compileError"*.

### 9. The column residue is one number on one line

The R1 insertion is the only text that moves a column. It moves one line, and it moves it by the
length of the inserted text. We measured `+32` for `let ``__fsHttpStudio_target`` = `. Each other
line and each R2 diagnostic is exact.

Carry the shift as `(line, offset)`. Subtract the offset from a diagnostic column on that line when
the column is at or after the insertion point. Leave a column before the insertion point unchanged.
Clamp a column that lands inside the inserted text to the insertion point.

**No `#line` directives are necessary.** The block's text is part of the Setup interaction, so its
diagnostics arrive on native source coordinates. Earlier research found that `#line` works in FSI.
That finding is correct and this mechanism does not need it. Do not add `#line` directives.

**No new dependency.** Do not add Fantomas. FSI takes a string, so each design prints the rewrite to
text, and both routes are in-place edits that keep each line. Fantomas would add a second F# parser
of about 2.4 MB for uniform implementation, and not for more coverage.

### 10. Apply the response-reading guard at invocation time

**This is a defect that the reach mechanism introduces. It is not in the prototype's results.**

`http { }` copies the global configuration into the context **when the block is built**. We measured
this against FsHttp 15.0.3: a context built before `GlobalConfig.set` carries
`httpCompletionOption = ResponseHeadersRead` and `bufferResponseContent = false`.

The companion appends its addendum after the Setup. With this change, the Run builds the block
**inside** the Setup, and thus before the addendum runs. This affects both routes, because the Setup
evaluates R1's synthesized binding and R2's `let` binding. The Run thus loses the response-reading
guard, and *"The stream was already consumed. It cannot be read again."* returns.

**Apply the guard to the block's value in the invocation.** The invocation becomes:

```
<qualified name> |> Config.update (fun c -> { c with bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead }) |> Request.send
```

We measured the following:

- `Config.update` accepts a built context and returns the same context type with the configuration
  applied. It works on FsHttp 13.2.0, 14.5.1 and 15.0.3, which is the version range that ADR-0002
  claims.
- Each context type that a block can produce implements the interface that `Config.update` needs:
  `HeaderContext`, `BodyContext`, `MultipartContext` and `MultipartElementContext`.
- The module's full name is `FsHttp.Dsl.Config`. The addendum's `open FsHttp` puts `Config` in scope
  for the invocation, because an `open` stays in effect for the later interactions of the session.

Keep the addendum as it is. Its `GlobalConfig.set` applies to a context that the Run builds at
invocation time, which is the `let f () = http { }` shape. The two guards are idempotent.

**Stated consequence.** The invocation now overrides these two settings for each Run, including a
Run of a block that sets them itself. FsHttp.Studio must read the whole body to render it, so this
override is a requirement of the product and not a preference.

### 11. A refused target returns a runtime error, until the refusal spec replaces it

The refusal-lens spec owns the refusal surface: the lens title, the toast, the wire code, and the
fourth Run outcome. That spec is blocked on this one. This spec must say what a Run does when it
reaches a refused position. A stale lens and the command palette both reach one.

**Do not evaluate.** Return a runtime error whose message names the position: *"FsHttp.Studio cannot
run a block in this position: a loop body describes many requests."*

A runtime error is the correct interim outcome. It reports that the Run produced no response, and it
does not put a red mark on correct user code, which a compile error would.

Two constraints on the message:

- The reason must be **readable by a user**. The prototype's fallback interpolates an FCS type name
  (*"an expression we cannot address (SynExpr)"*). It must not ship. Write a plain sentence.
- The reason strings are internal. The refusal-lens spec replaces this outcome and owns each shipping
  string, so do not review this text as final copy.

### 12. Write ADR-0007

Add `docs/adr/0007-run-setup-boundary-and-ast-reach.md`. It supersedes no ADR. Text slicing was never
a decision of record: `CONTEXT.md` and `README.md` describe it in prose only, and Decision 13
replaces those two statements.

Title, which states the promise:

> A Run evaluates everything above the target block and the block itself, and nothing after it

The body states why the block's own expression end is the boundary. It states that this choice
removes the side-effect widening and the case 12 double send.

`## Considered Options` gives the five mechanisms of the mechanism survey, one line each. It states
why we rejected text slicing: no block in the prototype's 32-block corpus needed it.

`## Consequences` states:

- R1 and R2 are the current mechanism, and they are **replaceable**. The boundary is the promise.
  When the routes change, a later ADR supersedes the mechanism and cites this one for the boundary.
- Routing is decided from the untyped syntax tree, with no type-check, no project load, and no NuGet
  resolution.
- The reserved identifier `` `__fsHttpStudio_target` `` is a permanent name in the user's namespace.
  It is used on the R1 route only. A user binding of the same name in the same scope gives a
  duplicate-definition error against the block's line. The Run does not avoid the collision.
- The column residue of `+32` on one line, on the R1 route only.
- ADR-0003 keeps tree-sitter "in reserve as a proven fallback". That reserve now buys less. Tree-sitter
  produces ranges. Routing needs the syntax tree to classify the block, to find the truncation point,
  and to compute the blank spans. This raises the cost of a reversal of ADR-0003.
- Some positions are not reachable without a type-check. The Run refuses them, and does not attempt
  them. Point at the refusal-lens spec. Do not name families, titles, or codes.
- Decision 13 of the spec replaces `CONTEXT.md`'s Setup entry and the `README.md` statement.

House style: 9 to 33 lines, a title that states the decision as a sentence, and a prose body. Add no
status header, no date header, and no template header. Match the ADRs that exist. `0007` is free.

Do not put the refusal policy in the ADR. "Refused Run" is a vocabulary change, and it belongs to the
refusal-lens spec.

### 13. Replace the two prose statements of the dead model

**`CONTEXT.md`, the Setup entry.** The current text says *"The code before a block that the block
depends on"*. Setup now includes the block. Use the `/domain-modeling` skill, which owns this file.
Proposed text:

> **Setup**:
> The code that a Run evaluates to reach the target block. It starts at the first line of the script,
> and stops at the end of the target block's own expression. It thus contains the target block,
> because a Run reaches a block where the user wrote it. It contains no other block, because
> FsHttp.Studio blanks each other block first. It contains nothing after the target block.
> FsHttp.Studio evaluates the Setup afresh for each Run.
> _Avoid_: context, preamble, prelude.

The first sentence avoids "send", which the **Run** entry's `_Avoid_` list rules out.

Keep the `_Avoid_` list without changes. Keep the **Run** entry without changes: a Run is the
evaluation of one block against a fresh evaluation of its Setup.

The "Run outcomes" section is **not** in this spec. The fourth outcome belongs to the refusal-lens
spec.

**`README.md`**, the bullet that starts *"Only that block runs"*. Proposed text:

> - Only *that* block runs. FsHttp.Studio blanks every other block, and then evaluates your script
>   from the top down to the end of that block. It stops there. Nothing after the block runs.

## Testing Decisions

### What makes a good test

Assert the **verdict** and the **outcome**. Do not assert the generated Setup text. The text is the
method, and it changes. The route that a position gets is the behavior, because the refusal-lens spec
puts that verdict in front of the user.

Keep `testSequenced`. Each end-to-end case starts an FSI session that resolves `#r "nuget:"` into the
process-wide package cache, and parallel cases race on that cache.

### Seam 1: the position matrix, with no FSI and no server

Drive `BlockLocator.locateBlocks` and assert each block's `Route`. This is a parse and a fold, which
we measured at 0.48 ms for a 175-line script, so the whole matrix is fast.

Assert 22 positions. Use one fixture script for #90's twelve cases, and one for the shapes that the
matrix does not have. Refer to Further Notes for the corpora that exist.

| # | Position | Verdict |
|---|---|---|
| 1 | bare, top level | R1 |
| 2 | bare, uses a preceding `let` | R1 |
| 3 | two independent bare blocks | R1 |
| 4 | right side of a `let`, block on the next line | R2 |
| 5 | right side of a `let`, block on the same line | R2 |
| 6 | body of a `()`-callable function | R2 |
| 7 | single block in a `module` | R1 |
| 8 | block in a `module` with a preceding member | R1 |
| 9 | in a `for` body | F1 |
| 10 | in an `if` branch | F1 |
| 11p | producer, `let dexId = http { }` | R2 |
| 11c | consumer, uses another block's value | R1 (refer to Further Notes) |
| 12 | piped to `Request.send` in the script | R1 |
| 13 | `let private x = http { }` | R2 |
| 14 | `module private` | R1 or R2, by shape |
| 15 | nested modules, `Outer.Inner.deep` | R1 or R2, by shape |
| 16 | attributed binding, `[<Obsolete>] let …` | R2 |
| 17 | `match` clause | F1 |
| 18 | `try`/`with` handler | F1 |
| 19 | function with arguments | F2 |
| 20 | lambda-valued binding | F3 |
| 21 | inner `let` in a function body | F3 |
| 22 | class member | F2 |
| 23 | tuple binding | F5 |
| 24 | block inside another block's expression | F5 |

Position 11c is **not** refused. It routes R1, because that is what it is. The syntax tree cannot know
that the name it needs was blanked away. Refer to Further Notes.

### Seam 2: the Run, as a black box against the counting server

Drive `BlockRunner.run` with `.fsx` source and a block index, which is the seam that
`BlockRunnerTests` uses today. Add these cases:

1. **Each supported shape sends only one request.** One case for each of: a bare top-level block,
   a block in a nested module, a `let`-bound block, and a `()`-callable function's block. Assert
   `Ok`, and assert that the server counted one request.
2. **Case 12 sends one request.** The script pipes the block to `Request.send`. Assert the count is
   1, and not 2. This is the isolation guard that the boundary must not lose.
3. **Nothing after the block runs.** A module holds a block, and then a raw `HttpClient` call to a
   second path in the same module, below the block. Run the block. Assert that the second path was
   not requested. This is the test for the boundary in Decision 1.
4. **The clicked block survives a shared statement span.** `let a, b = http { }, http { }`. Both are
   refused as targets, so use the hazard's runnable shape instead: a block in another block's
   expression, where the other block's blank span contains the target. Assert that the target's Run
   does not fail with *"is not defined"* against its own name.
5. **A class-member block does not break a different block's Run.** The script has a type with a
   member that holds a block, and a runnable block above it that names the type. Run the block above.
   Assert `Ok`.
6. **A `private` binding runs.** `let private secret = http { }`. Assert `Ok`. Add a block in a
   `module private`. Assert `Ok`.
7. **An `internal` binding runs with no treatment.** Assert `Ok`. This records the measurement.
8. **An attributed binding runs.** `[<Obsolete>] let x = http { }`. Assert `Ok`. This guards the
   keyword search against the attribute line.
9. **A type annotation that the truncation contradicts does not stop the Run.**
   `let x : Response = http { } |> Request.send`. Assert `Ok`, and assert one request. This is
   the test for Decision 7.
10. **The streamed body stays readable on both routes.** The test that exists covers the bare block,
    which is R1. Add the same body-after-headers server with a `let`-bound block, which is R2. Run
    each three times. This is the guard for Decision 10, and it is the case that fails if the
    invocation does not carry the configuration.
11. **A block compile error keeps its own message and position.** The test that exists must pass.
    Add an assertion that the message does not carry the Setup introduction of #95.
12. **A Setup compile error keeps the Setup introduction.** The test that exists must pass. This pair
    is the test for the split in Decision 8.
13. **A diagnostic on the block's first line reports the true column.** Put an undefined name on the
    block's first line, on the R1 route. Assert the reported column, which proves that the shift is
    subtracted. Put one on a later line and assert that the column is unchanged.
14. **A refused position returns a runtime error that names the position.** Use the `for` body. Assert
    a runtime error, and assert that the message does not contain `http`. Assert that no request
    reached the server.

Reuse the counting handler and the streaming handler that `TestServer` has. Add no test project.

### The tests that change

`BlockLocatorTests` has two tests for the `Statement` field: *"locateBlocks pairs a let-bound block
with its whole statement, including a trailing pipe"* and *"locateBlocks pairs a bare block statement
with itself"*. The field is removed. Replace them with tests for the `Blank` span, which include the
member split in Decision 5.

## Out of Scope

- **The refusal surface.** The lens title, the toast, the wire code, the fourth Run outcome, and the
  correction to `CONTEXT.md`'s Run outcomes belong to the refusal-lens spec, which is blocked on this
  one. This spec produces the verdict and an interim runtime error only.
- **The swallowed Setup diagnostics (#94, spec #95).** Land that change first. It is independent, and
  it makes a failure of this work legible. Decision 8 amends its wording rule, and does not repeat it.
- **The session model (#90 §2).** A block that needs another block's value stays unrunnable. Decision 4
  is deliberately what makes it fail cleanly.
- **Blocks in `.fs` files.** ADR-0004 fixes `.fsx` as the only source surface. "Run works everywhere"
  means each position in a script.
- **The `blocks` envelope and the extension host.** No wire change is in this spec. The refusal-lens
  spec adds the refusal code.
- **The status line's method and URL.** It reads the block's text with a heuristic, and it is
  unchanged here. A separate spec owns it.
- **Performance work.** The mechanism costs 0.004 ms against a session of about 577 ms. There is
  nothing to trade off. v0.2 makes no performance claim.
- **A collision-avoidance rule for the reserved identifier.** ADR-0007 records the collision behavior.

## Further Notes

### The prototype, and what it proved

A throwaway prototype ran this mechanism end to end against a request-counting server, across two
corpora of 32 blocks. 31 of 32 matched the matrix. Each supported position sent only one request.
Each refused position was decided from the untyped syntax tree.

It also proved three negatives that shorten this work:

- **Text slicing is dead.** No block in 32 routed to it, which includes case 12, the last reason to
  keep it.
- **No `#line` directives are necessary.**
- **The boundary holds at each nesting depth.** The side-effect probe never fired.

The prototype is at `.local/wayfinder/v0.2/prototypes/004-run-plan/` on the map owner's machine.
That directory is not in the repository. `RunPlan.fs` is the reference implementation of Decisions 1
to 6 and 9, and `cases/matrix.fsx` and `cases/extra.fsx` are the two corpora that Seam 1 needs. Copy
them into the test project before you start.

### The one divergence: position 11c

A block that uses a name that another block bound routes R1, like each other bare block. Nothing in
the untyped syntax tree says that the name will have been blanked away. Only the Run finds out.

This is correct, and it is why F4 is a **Run outcome** and not a classify verdict. The refusal-lens
spec owns it. It recognizes the case by the diagnostic number for an unbound name, against the names
that the blanking removed. Decision 4 of this spec is what makes that recognition precise, because
statement-level blanking gives a clean *"is not defined"* message.

### Corrections that this work carries to the reply to #90

- SchlenkR called each verdict correctly. Two causes were wrong. **Case 8 is a scope failure**, and
  not the indentation failure that the matrix predicted. **Case 9 is swallowed Setup diagnostics**,
  and not the loop.
- **Case 8 needs no "auto-mode" feature.** The module is submitted as a unit, so its own `let` is in
  scope. Case 8 costs nothing beyond case 7.
- **Case 10 does not type-check as written.** An `if` with no `else` that returns a context is not
  `unit`. The file only parses, which is why a lens appears at all.
- **`private` is a thirteenth position that the matrix does not have**, and it is supported.
- The twelve cases generalized to 22 positions with **no new rules**.

### Provenance

The decisions come from earlier planning work on this feature. Four tickets supplied them:

- a research ticket that measured five mechanisms on a coverage grid,
- a grilling ticket that fixed the runnability matrix and the nine constraints,
- the prototype above,
- a grilling ticket that decided the ADR.

The measurements in Decision 10 are new, and this spec made them. The prototype's harness appended
the addendum after the Setup, as the companion does. Its 32 blocks ran, because the local server
returns the body with its headers. The defect needs a body that streams after its
headers, which the repository's own regression test uses. We measured the configuration on the built
context to confirm the cause, and we measured `Config.update` against FsHttp 13.2.0, 14.5.1 and
15.0.3 to confirm the fix.
