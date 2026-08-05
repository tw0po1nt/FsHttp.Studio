# The Run request lens tells the truth, and Refused Run

Spec for #90 §3.

This is the second half of v0.2's correctness work. The reach spec (#96) makes a Run reach nearly
every position. This spec makes the lens tell the truth about the positions that are left.

## Problem Statement

`▶ Run request` appears above **every** block that `BlockLocator` finds. Nothing judges whether the
Run can deliver. The lens is therefore a promise that FsHttp.Studio breaks for each position that it
cannot reach.

The user clicks the lens, and the response viewer reports *"The value or constructor 'http' is not
defined"*. That message names the one identifier in the script that is correct. The user reads it as
a defect in their own script.

Schlenkr reported this in #90 §3, beside the reach failures of §1.

The reach spec closes most of the gap: 18 of 22 positions now run. Four do not, and the reach spec
returns an interim runtime error for them, which it marks as this spec's to replace. Two further
paths reach a refused block with no lens click at all:

- **A stale lens.** The user edits the script, the block indices shift, and the Run lands on a
  different block.
- **The command palette.** `fshttpStudio.runBlock` is contributed at `package.json`, and its handler
  unboxes the lens arguments unconditionally (`RunCommand.fs:81-82`). A palette invocation passes
  `undefined`, and the block index is garbage.

And one position cannot be judged before the Run at all. Matrix case 11c is a block that uses a name
that another block binds. It is a bare expression, it routes R1, and nothing in the untyped syntax
tree says that the name it needs was blanked away. Only the Run finds out.

## Solution

**A refused block keeps its lens, and the lens states the refusal in its title.**

`▶ Run request` becomes `⊘ Cannot run: inside a loop`. A click shows a warning toast with the reason
and, where the position has one, the workaround. No Run starts, no response viewer opens, and no
companion round trip is spent to say no.

The title is **shape-grained**. There are twelve of them, one for each verdict that `classify`
produces, and not one for each refusal family. The families stay internal.

**The companion is the gate, not the lens.** The `run` path classifies before it evaluates and
refuses on the same codes. The lens is a cheap early surface of the rule, and it is not the
enforcement. This closes the stale-lens path and the palette path.

**A refused Run is a fourth Run outcome.** "Refused Run" joins HTTP error response, runtime error,
and compile error in `CONTEXT.md`. It has its own surface in the response viewer, so a refusal never
puts a red mark on correct user code. Case 11c is recognized precisely, by the diagnostic number for
an unbound name against the names that the blanking removed.

## User Stories

1. As a script author with a block in a loop, I want the lens to tell me before I click, so that I
   do not wait for a Run that cannot start.
2. As a script author, I want the refusal to name my position, so that I do not read a message about
   `http`.
3. As a script author, I want the refusal to tell me what to do instead, so that I can move the
   request and run it.
4. As a script author, I want a block that FsHttp.Studio cannot run to keep a lens, so that a
   missing lens does not read as a failure to find my request.
5. As a script author, I want no response viewer to open for a refusal that FsHttp.Studio knows in
   advance, so that a refusal does not look like a failed Run.
6. As a script author whose lens is stale after an edit, I want the Run to refuse, so that a shifted
   index does not produce a message about my own code.
7. As a script author, I want a request that depends on another request's value to say so, so that I
   do not look for a typo in a name that is correct.
8. As a script author, I want a refusal to look different from a runtime error, so that I can tell
   what FsHttp.Studio declined from what my script broke.
9. As a script author, I want each refusal to cost no package resolution and no child process, so
   that a refusal is immediate.
10. As a user of the command palette, I want no broken "Run request" entry, so that the palette does
    not offer an action that cannot work.
11. As a maintainer, I want each shipping string in one file, so that a wording change touches no
    process boundary.
12. As a maintainer, I want an unknown refusal code to degrade, so that a stale host and a new
    companion do not break the lens.

## Implementation Decisions

### 1. The policy: a refused block keeps a lens, and the lens states the refusal

For a block that `classify` refuses, `CodeLensProvider` builds a lens with a refusal title and the
command `fshttpStudio.explainBlockRefusal`. The command shows a warning toast and does nothing else.

Three alternatives were considered and rejected:

- **No lens at all.** A block with no lens reads as a block that FsHttp.Studio failed to find, which
  is its own confusion. This is the runnability matrix's finding, and nothing here changes it.
- **A normal lens that fails loudly.** This preserves exactly the broken promise that #90 §3
  reported. It survives for case 11c only, where the lens cannot know.
- **One generic disabled title.** This makes the user click to learn something that the lens could
  have stated, on every refused block. That click is the click that the policy exists to remove.

**The lens keeps its index.** A refused block still contributes one entry to the `blocks` response
and one lens, in source order. `RunCommand` and `CodeLensProvider` both index into the same list, so
no index arithmetic changes anywhere.

### 2. Twelve shape codes, and the host owns every string

The companion ships a **code**. The host maps the code to the lens title and the toast. Three
reasons:

- Every shipping string sits beside the existing `▶ Run request` title, so one file gets the
  controlled-language review.
- A wording change never touches the companion or the wire.
- An unrecognized code degrades to the generic refusal instead of breaking the lens. The companion
  and the host ship in one `.vsix`, but a stale one can survive an upgrade.

Duplicating the vocabulary across the boundary is this repository's convention, stated at
`Protocol.fs:8` for `BlockRange`.

`classify` produces thirteen verdicts today, and two of them mean the same thing to a reader: a
`SynBinding` under neither a module `let` nor a member, and a `SynExpr.LetOrUse` ancestor. Both are a
local binding. They share one code. The remaining eleven map one to one.

| Code | Family | Lens title | `classify` branch |
|---|---|---|---|
| `loopBody` | F1 | `⊘ Cannot run: inside a loop` | `For`, `ForEach`, `While` |
| `ifBranch` | F1 | `⊘ Cannot run: inside an if branch` | `IfThenElse` |
| `matchClause` | F1 | `⊘ Cannot run: inside a match clause` | `Match`, `MatchLambda`, a `SynMatchClause` under a `Match` or a `MatchLambda` |
| `exceptionHandler` | F1 | `⊘ Cannot run: inside a try block` | `TryWith`, `TryFinally`, a `SynMatchClause` under a `TryWith` |
| `needsArguments` | F2 | `⊘ Cannot run: this function needs arguments` | `derivedName` gives `TakesArguments` |
| `classMember` | F2 | `⊘ Cannot run: inside a class member` | `SynMemberDefn`, `SynTypeDefn` parent |
| `innerBinding` | F3 | `⊘ Cannot run: inside a local binding` | non-module binding, `LetOrUse` |
| `lambdaValue` | F3 | `⊘ Cannot run: this binding holds a function` | `SynExpr.Lambda` |
| `noNameToCall` | F3 | `⊘ Cannot run: this binding has no name` | `derivedName` gives `NoName` |
| `tupleBinding` | F5 | `⊘ Cannot run: this binding binds two or more values` | `SynExpr.Tuple` |
| `insideAnotherRequest` | F5 | `⊘ Cannot run: inside another request` | Refer to Decision 3 |
| `unaddressable` | F5 | `⊘ Cannot run in this position` | the catch-all |

`unaddressable` is also the title that an unrecognized code degrades to.

FCS gives a `with` case the same node as a `match` clause: `SynMatchClause`. The clause alone cannot
select the code. `classify` reads the parent. A `SynMatchClause` under a `TryWith` gets
`exceptionHandler`. Every other `SynMatchClause` gets `matchClause`. This order keeps each title on
the shape that the user wrote.

The glyph is `⊘`, and it replaces `▶`. The two are distinct at a glance, and the title never starts
with the run triangle that it cannot honor.

**The shipping strings say "request", and not "block".** Block is the glossary's word, and this spec
uses it. The user-facing word is the one that `▶ Run request` already uses.

### 3. `insideAnotherRequest` comes from range containment, not from the syntax tree

The reach spec's position table gives verdict F5 to "a block inside another block's expression". The
prototype has no branch for it. It falls into the catch-all, whose text interpolates an FCS type name
(*"an expression we cannot address (SynExpr)"*), which the reach spec already marks as unshippable.

Decide it without a new syntax-tree branch. `locateBlocks` returns every block in the script.
**A target whose range is strictly inside another located block's range gets `insideAnotherRequest`.**
Apply this test after `classify`, and only when `classify` produced the catch-all.

This makes the reach spec's position 24 precise, and it leaves `unaddressable` for the shapes that
nobody has enumerated.

### 4. The wire: one optional field on a block range, one new run tag

**`blocks`.** Each range in the `blocks` response gains an optional `refusal` string. A supported
block omits it. Add nothing else. The host cannot act on a route or an invocation, and the companion
re-derives both at Run time from the same source.

```json
{ "tag": "blocks",
  "ranges": [ { "startLine": 3, "startCol": 0, "endLine": 5, "endCol": 1 },
              { "startLine": 9, "startCol": 4, "endLine": 11, "endCol": 5,
                "refusal": "loopBody" } ] }
```

`Protocol.BlockRange` gains `Refusal: string option`. `Companion.toBlockRange` must read an absent
property as `None`, and not `unbox` it. Each other field keeps its unconditional read.

**`run`.** A fourth response tag joins `ok`, `compileError`, and `runtimeError`:

```json
{ "tag": "refused", "code": "loopBody" }
{ "tag": "refused", "code": "unboundBlockValue", "name": "dexId" }
```

`BlockRunner.RunOutcome` gains `Refused of code: string * name: string option`, and `outcomeToWire`
and `wireToOutcome` both carry it, so the `--worker` channel and the host channel stay identical.
`Protocol.RunResult` gains `RunRefused of code: string * name: string option`.

`name` is present for `unboundBlockValue` only. Refer to Decision 7.

### 5. The gate runs before the pin reservation

The reach spec puts `classify` in `BlockLocator`, so `run` can ask for the verdict without building
any text. **Refuse in `BlockRunner.run`, before `routeAndReserve`.**

This is not only a tidiness point. `routeAndReserve` marks each of the Run's pins in `loadedVersions`
**up front**, before any evaluation, and its own comment states why: an over-mark is the safe error,
and it costs a later Run a cold worker. A refusal that passes through it therefore over-marks pins
that no session ever loaded, and slows a later Run for nothing. Refusing first also spends no
`--worker` process on a Run that cannot start.

`locate` plus `classify` costs 0.48 ms for a 175-line script, against a session of about 577 ms.

An out-of-range block index keeps today's runtime error
(`BlockRunner.fs:220`). A stale lens that shifts onto a different **valid** index gets that block's
own verdict, which is the correct answer.

This replaces Decision 11 of the reach spec (#96), which returns an interim `RuntimeError` naming the
position. Delete that interim, and its reason strings with it.

### 6. The refusal surface in the response viewer

A refused Run reaches the viewer through the stale-lens path, the palette path, and case 11c. The
viewer must not render it as a runtime error. Reporting our own refusal in the surface that reports
the user's broken code is the confusion that #90 §3 named.

Add a `refused` message tag to the host-to-webview protocol, beside `running`, `result`, and `error`
(`webview/Main.fs:67-76`):

```
{ "tag": "refused", "title": "…", "detail": "…" }
```

`Main.fs` renders it into a container with a heading and a body paragraph, and it clears the pending
timer like each other terminal message. `title` is the lens title without the glyph, in sentence
form. `detail` is the same text that the toast shows.

Style it as a notice, and not as an error: use `--vscode-descriptionForeground` for the body and the
editor foreground for the heading, in the existing `responseStyles`. Add no color that reads as a
failure.

### 7. Case 11c: recognize the unbound name, do not guess it

Case 11c evaluates. The Setup blanks the producer's statement, so the consumer's name has no binding,
and FCS reports it.

**The rule.** Statement-level blanking (reach spec, Decision 4) means that the companion knows
exactly which names it removed. Collect them while blanking. Then:

- An error diagnostic with `ErrorNumber` **39** that names one of those names is a **Refused Run**,
  with code `unboundBlockValue` and that name.
- An error diagnostic with `ErrorNumber` 39 that names anything else is a **compile error**. That one
  is the user's own typo.

Match on `ErrorNumber`, and not on the message text, which is localized. Extract the name from the
diagnostic's own range against the Setup text, and not by a parse of the message.

**Precedence.** Apply this test to the Setup interaction's error diagnostics before the compile-error
path builds its list. When the diagnostics hold both a blanked-name FS0039 and an unrelated error,
report the compile error. A refusal claims the Run only when the missing binding is the whole story.

**Refuse the whole Run.** Do not evaluate the invocation after this test matches.

The lens for case 11c stays `▶ Run request`. It is not lying. It cannot know, and the Run is where
the truth arrives.

### 8. The new command, and the toast

Register `fshttpStudio.explainBlockRefusal` in `RunCommand.fs`, with the same two arguments that
`runBlock` takes. It reads the block's refusal code and shows a warning toast. It does not touch the
response viewer, the generation counter, or the companion.

The toast has **no button**. `Vscode.IWindow.showWarningMessage` takes a message and one item today
(`Vscode.fs:101`). Add a message-only overload. Do not pass a dummy item.

**Do not contribute `explainBlockRefusal` to `contributes.commands`.** A registered command that the
manifest does not declare works for a lens click and does not appear in the palette, which is what
this command needs.

### 9. Drop `runBlock` from the command palette

Remove the `contributes.commands` entry for `fshttpStudio.runBlock` from `package.json`. The lens
click does not need it, and the palette cannot supply the two arguments that the handler expects.

`contributes.commands` then holds no commands. Remove the empty key.

We considered a `menus.commandPalette` guard with `when: false`, which keeps the declaration. It adds
a second contributes block to hide an entry that has no reason to exist. We also considered making
the palette entry work, by falling back to the active editor and the block at the cursor. That is new
behavior, and v0.2's feature budget is spent.

**Also guard the handler.** `RunCommand`'s handler unboxes `documentArg` and `indexArg`
unconditionally. Return early when either is nullish. The manifest change closes the one path that
reaches it today, and the guard costs one line and survives a future re-contribution.

### 10. The strings

Each lens title is in Decision 2. The toasts and the viewer details follow. One host module owns
them, beside the `▶ Run request` title.

| Code | Toast and viewer detail |
|---|---|
| `loopBody` | FsHttp.Studio cannot run a request inside a loop. A loop body describes many requests, and one Run sends one request. To run this request, bind it to a name outside the loop, then run that binding. |
| `ifBranch` | FsHttp.Studio cannot run a request inside an if branch. The script chooses the branch when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the if, then run that binding. |
| `matchClause` | FsHttp.Studio cannot run a request inside a match clause. The script chooses the clause when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the match, then run that binding. |
| `exceptionHandler` | FsHttp.Studio cannot run a request inside a try block. The script chooses the handler when it runs. To run this request, bind it to a name outside the try, then run that binding. |
| `needsArguments` | FsHttp.Studio cannot run a request in a function that takes arguments, because it has no values to supply. To run this request, move it to a binding that takes no arguments. |
| `classMember` | FsHttp.Studio cannot run a request in a class member, because it has no instance of the class. To run this request, move it to a module-level binding. |
| `innerBinding` | FsHttp.Studio cannot run a request in a local binding. A local binding is not in scope after the script runs. To run this request, move it to a module-level binding. |
| `lambdaValue` | This binding holds a function, and not a request. FsHttp.Studio sends the request only when your code calls the function. To run this request, bind it directly to a name. |
| `noNameToCall` | The pattern of this binding gives FsHttp.Studio no name to call. To run this request, bind it to a simple name. |
| `tupleBinding` | This binding binds two or more values, so its value is not the request alone. To run this request, give it its own let binding. |
| `insideAnotherRequest` | This request is inside another request. FsHttp.Studio can run the outer request only. To run this request, move it to its own binding. |
| `unaddressable` | FsHttp.Studio cannot address a request in this position. To run this request, move it to its own let binding, at the top level of the script or of a module. |
| `unboundBlockValue` | This request uses `{name}`, which another request in this script binds. One Run evaluates one request, so `{name}` has no value. FsHttp.Studio cannot run a request that depends on another request. |

`unboundBlockValue` has no lens title. It is a Run outcome only.

Rules that these strings hold, and that a later edit must hold:

- No contractions, active voice, and American spellings, per `AGENTS.md` and the controlled-language
  skill.
- Name the position, then state why, then state the corrective action.
- State no workaround that does not exist. `unboundBlockValue` states the limit and promises no
  session model.

### 11. `CONTEXT.md`: one new Run outcome, one corrected example

Add the fourth outcome to the "Run outcomes" section:

> **Refused Run**:
> A Run that FsHttp.Studio declined, because it cannot reach the block's position, or because the
> block depends on a value that another block binds. No code was evaluated for a position refusal.
> The response viewer reports the reason and the workaround, and it does not report a fault in the
> user's script.
> _Avoid_: unsupported, blocked, disabled.

**And correct the Runtime error entry.** It reads:

> A Run that produced no response, because the user's code or the host failed. Examples are a refused
> connection, or **a name that an earlier un-run block left unbound**.

That example is case 11c, which is the new outcome, and the shipping code routes it to `CompileError`
today (`BlockRunner.fs:265-274`, where any error diagnostic outranks the exception path). Remove the
example. Leave the refused-connection example, and leave the `_Avoid_` list unchanged.

Use the `/domain-modeling` skill, which owns this file.

## Testing Decisions

### Seam 1: the code, at the pure boundary

Drive `BlockLocator.locateBlocks` and assert each block's refusal code. This is the same parse and
fold that the reach spec's Seam 1 uses, so extend that fixture set rather than adding a project.

Assert one case for each of the twelve codes. The reach spec's corpora already hold ten of the
positions. Add a fixture for `noNameToCall` and one for `insideAnotherRequest`.

Assert that a supported position carries **no** code.

### Seam 2: the gate, and the outcomes

Drive `BlockRunner.run`, which is the seam that `BlockRunnerTests` uses today.

1. **A refused position returns `refused` with its code, and sends nothing.** Use the `for` body.
   Assert the tag, assert the code, and assert that the counting server saw no request.
2. **The refusal spends no session.** Assert that a refused Run against a script whose `#r "nuget:"`
   pins a package leaves `loadedVersions` unmarked, so a later Run of a different pin stays
   in-process. This is the test for Decision 5.
3. **Case 11c returns `unboundBlockValue` and the name.** The producer binds `dexId`, and the
   consumer uses it. Assert the code and assert `name = "dexId"`.
4. **A real typo stays a compile error.** The same script, with an undefined name that no block
   bound. Assert `compileError`, and assert that the tag is not `refused`.
5. **A typo beside a blanked name stays a compile error.** Both errors in one script. This is the
   precedence rule in Decision 7.
6. **An out-of-range index stays a runtime error.** The stale-lens boundary case.

### Seam 3: the host's map

`tests/host.Tests` drives `Protocol` directly. Add:

7. **Each of the twelve codes maps to a title and a detail.** Assert that no title and no detail is
   empty, and assert that the twelve titles are distinct.
8. **An unknown code degrades.** Assert that it maps to the `unaddressable` title and detail, and
   does not throw.
9. **A block range with no `refusal` property decodes to `None`.** This is the compatibility rule in
   Decision 4, and the one that a stale companion exercises.

### What is not tested here

No test drives the VSCode lens surface or the toast. `CodeLensProvider` and the warning toast are
interop, and ADR-0003's seam puts the testable logic in `Protocol`. Seam 3 covers the map, which is
where a wording defect lives.

## Out of Scope

- **A README section or a docs anchor for the refused positions.** The guidance is worth more in the
  toast, at the moment of confusion, than in a document that the user must find. Twelve shapes in two
  places also drift. Decided in the deciding ticket, and it closes the map's README question as no.
- **A quick fix that applies the workaround.** Naming the workaround is this spec's job. Performing
  it is a refactoring feature, and v0.2's feature budget is spent.
- **A palette command that runs the block at the cursor.** Refer to Decision 9.
- **The session model (#90 §2).** `unboundBlockValue` states the limit and promises nothing.
- **A type-check anywhere on the lens path.** The runnability matrix settled this. A script is broken
  most of the time while the user writes it, and lenses that vanish and return on each keystroke
  would be worse than the defect that this spec fixes.
- **The reach mechanism itself.** Spec #96 owns `classify`, the routes, and the blanking. This spec
  consumes the verdict and the blanked names.
- **The swallowed Setup diagnostics.** Spec #95 owns them, and it is a hard prerequisite. A swallowed
  setup error names the wrong problem before any string in this spec is reached.

## Further Notes

### Dependencies, in order

1. **#95**, the swallowed Setup diagnostics. Every message here assumes that a setup error is
   reported and does not surface as *"the value or constructor 'http' is not defined"*.
2. **#96**, the reach mechanism. It provides `classify` in `BlockLocator`, the statement-level
   blanking that Decision 7 reads, and the seam that Decision 5 calls before `routeAndReserve`.

This spec deletes one thing that #96 adds: the interim refused-target `RuntimeError` of its
Decision 11, which #96 itself marks as this spec's to replace.

### The count of codes

The deciding ticket estimated "roughly eleven, one per `classify` outcome". The count is twelve,
because two `classify` branches merged into `innerBinding` and one new branch appeared as
`insideAnotherRequest` (Decision 3). Thirteen strings ship, because `unboundBlockValue` is a Run
outcome with no lens.

### Provenance

The decisions come from earlier planning work on this feature. The policy, the code-not-prose split, the gate on the
`run` path, the fourth Run outcome, the FS0039 recognizer, and the fifth refusal family were decided
in a grilling ticket that read the shipping source. The `classify` verdicts and the position table
come from the prototype behind #96.

Three decisions are new in this spec, and the source reading produced each one:

- **The gate goes before `routeAndReserve`**, because that function reserves pins up front, so a
  refusal that passes through it slows a later Run.
- **`insideAnotherRequest` comes from range containment**, which makes #96's position 24 precise
  without a new syntax-tree branch and retires the catch-all's FCS type name.
- **The precedence rule** when a blanked-name FS0039 and an unrelated error arrive together.

Two defects that the deciding ticket found are fixed here, in Decisions 9 and 11.
