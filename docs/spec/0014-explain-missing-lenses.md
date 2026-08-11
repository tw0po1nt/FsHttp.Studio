# Explain why a script has no request lenses

Spec for v0.2 feature 3 of 3, from the discovery direction of the deciding ticket.

This is the smallest of v0.2's three features, and the only one that answers a question the user
asks about FsHttp.Studio itself: *why is there nothing here?*

## Problem Statement

`▶ Run request` appears above each block that `BlockLocator` finds. When no lens appears,
FsHttp.Studio says nothing at all. The user has four possible reasons and no way to tell them apart:

- The companion is not ready (`CodeLensProvider.fs:54`).
- The file is not a `.fsx` script (`CodeLensProvider.fs:54`, ADR-0004).
- The parse found no `http { }` block.
- The builder has a name other than `http` (`BlockLocator.fs:37`).

One of the four actively confuses. **The script stopped parsing, so lenses that the user looked at
one keystroke ago are gone.** VSCode requests the lenses again on each document change, so the loss
is immediate and silent.

### What the parse actually does, measured

A probe put 26 sources through the shipping locator's exact parse and fold. The result corrects the
premise that this spec started from:

| Damage | Example | Blocks located |
|---|---|---|
| Below every block | unclosed `{`, unterminated string, `let c =` at the end of the file | Each real block survives. Two cases also produced a lens for the half-written block. |
| Above the blocks | unterminated string, unterminated comment, `#if` with no `#endif` | 0 of 2 |
| Between the blocks | a stray `}`, a line that is not F# | 1 of 2 |
| At line 1, where recovery resynchronized | `let a = (` | 1 of 2, and it kept the **second** block and lost the **first** |

Three facts come from this table:

1. **Lenses do not vanish while the user adds a block at the end of a script.** FCS error recovery
   keeps each earlier block. Total loss needs damage above the blocks.
2. **Partial loss is real, and it is the worse case.** Some lenses stay and others go. Which blocks
   survive does not follow from the position of the error, so FsHttp.Studio can never report the
   count that is missing. It does not know it.
3. **`ParseHadErrors` is a sound trigger.** Each parse diagnostic in all 26 cases had severity
   `Error`. No parse-level warning appeared.

### The status bar reports the wrong thing

`Extension.fs:105-108` creates one status bar item, calls `show()` once, and never hides it. Its text
is a function of companion state alone, so it reads `FsHttp.Studio: ready` in a TypeScript file, and
it reads the same in an `.fsx` script where FsHttp.Studio found nothing.

## Solution

**Two surfaces, and one boolean on the wire.**

**In the editor**, and only when the script does not parse *and* the locator found no block, one
CodeLens at the top of the file states the reason. This is the state where the user faces an empty
script and has no explanation.

**On the status bar**, the item becomes aware of the active document. It reports what FsHttp.Studio
found in the script that the user is looking at, and it hides itself in a document that is not F#.

**On the wire**, the `blocks` response gains one boolean, `parseFailed`.

FsHttp.Studio does not report the syntax error itself. `Protocol.fs:38-40` already decided that
Ionide owns the editor, and Ionide reports the parse error at the character that caused it. This
spec explains the **absence of the lenses**, which is the part that no other extension can explain.

## User Stories

1. As a script author whose script stopped parsing, I want the editor to tell me why my lenses are
   gone, so that I do not read the loss as a broken extension.
2. As a script author, I want that message to appear only when each lens is gone, so that a lens does
   not appear and disappear on each keystroke while I write a request.
3. As a script author, I want the message to name the syntax error, so that I look at the Problems
   panel and not at FsHttp.Studio.
4. As a script author with a healthy script, I want the status bar to tell me how many requests
   FsHttp.Studio found, so that I know that it read my script.
5. As a script author in a `.fs` file, I want the status bar to tell me that requests need an `.fsx`
   script, so that I do not look for a defect.
6. As a script author whose script lost some lenses and kept others, I want the status bar to say
   that a syntax error can hide requests, so that I do not trust an incomplete count.
7. As a user of a TypeScript file, I want no FsHttp.Studio item in my status bar, so that an
   extension for F# scripts stays out of my way.
8. As a maintainer, I want the status bar and the lenses to come from one response, so that the two
   surfaces cannot disagree.
9. As a maintainer, I want each string and each rule in a module with no VSCode interop, so that
   tests assert them.

## Implementation Decisions

### 1. The trigger: the parse failed **and** the locator found no block

Show the lens only for that pair. Two alternatives were considered and rejected:

- **A failed parse alone**, which is what the deciding ticket asked for. The probe shows that this
  fires on nearly every keystroke while a user writes a new block at the end of a file, in a state
  where each lens is present and nothing is missing. A CodeLens at line 1 also pushes the whole
  document down by one line, so the flicker moves the text under the user's cursor.
- **A count below the count of the last clean parse of this document.** This detects partial loss
  precisely. It costs per-document state in the host, and it gives nothing for a script that is
  already broken when the user opens it, which is the case that this feature is named for.

**Partial loss goes to the status bar** (Decision 5), which does not move the text and does not
flicker.

### 2. The lens: line 1, one string, no command

Build one `CodeLens` at line 0, column 0 in VSCode coordinates, which is line 1 of the file.

Title:

```
⊘ No requests found: this script has a syntax error
```

The title leads with the absence, because the absence is the user's question, and then states the
cause. *Syntax error* is the term that Ionide and the Problems panel already use for this defect, so
the two agree.

**The lens carries no command.** A CodeLens with a title and no command renders as plain text and
does not look like a control that does nothing. The title carries the whole message, and a toast
that repeats a visible message adds nothing. Refer to Out of Scope for the two click targets that
were rejected.

The glyph is `⊘`, which #97 uses for a request that FsHttp.Studio declines. **The prefix is not
#97's `⊘ Cannot run:`**, because that prefix means "this request is refused", and this lens refers
to no request.

### 3. The wire: one boolean, and its polarity is load-bearing

The `blocks` response gains one envelope-level property:

```json
{ "tag": "blocks",
  "parseFailed": false,
  "ranges": [ { "startLine": 3, "startCol": 0, "endLine": 5, "endCol": 1 } ] }
```

**The host must read an absent `parseFailed` as `false`.** This follows #97's compatibility rule for
an absent `refusal`. The polarity is the reason for the name: the opposite spelling, `parsed`, reads
an absent property as "did not parse", and a companion that does not send the property would then
light the lens on each response.

Add nothing else to the envelope. The error position and the error message have no consumer after
Decision 2, the probe measured that FCS reports the position of the recovery point and not the
damage, and one message ran to two lines and named a compiler flag.

### 4. The companion: the flag comes from `ParseHadErrors`

`BlockLocator.parse` already holds the `FSharpParseFileResults`. Return its `ParseHadErrors` beside
the blocks.

```fsharp
type LocateResult =
    { Blocks: LocatedBlock list
      ParseFailed: bool }
```

`RequestHandler`'s `locate` arm reads both fields and writes both properties.

Use `ParseHadErrors`, and not a filter over `Diagnostics`. The probe found no parse-level warning in
26 cases, so the two agree on each measured case, and the flag needs no severity rule.

### 5. The status bar: aware of the active document, fed by the lens response

The item's text becomes a function of two inputs: companion state, and a view of the active document.

| Companion | Document | Parse | Blocks | Text |
|---|---|---|---|---|
| any | not F# | — | — | *the item is hidden* |
| `Starting` | F# | — | — | `starting…` |
| `SdkNotFound` | F# | — | — | `.NET SDK not found` |
| `Stopped` | F# | — | — | `companion stopped` |
| `Ready` | F#, not `.fsx` | — | — | `not an .fsx script` |
| `Ready` | `.fsx`, no response yet | — | — | `looking for requests…` |
| `Ready` | `.fsx` | clean | 1 | `1 request` |
| `Ready` | `.fsx` | clean | N > 1 | `N requests` |
| `Ready` | `.fsx` | clean | 0 | `no requests found` |
| `Ready` | `.fsx` | failed | 0 | `no requests found — syntax error` |
| `Ready` | `.fsx` | failed | N ≥ 1 | `N requests — a syntax error can hide others` |

`Extension.setStatusText` supplies the `FsHttp.Studio: ` prefix, which is unchanged.

**The data comes from the `blocks` response that the lenses already come from.** `provideCodeLenses`
sends one `locate` on each document change. The status bar reads that same response, so the count in
the status bar always equals the count of lenses on the screen. A second `locate` for the status bar
would double the round trips per keystroke and would admit a state where the two surfaces disagree.

**`CodeLensProvider` does not touch the status bar item.** It calls one supplied callback,
`onLocated: TextDocument -> ScriptView -> unit`, which `Extension` wires to the item. `Extension`
keeps ownership of the item, which it has today.

**The callback must ignore a document that is not the active editor's document.** VSCode requests
lenses for each open document, and two editors can be visible at once. Compare the document against
`window.activeTextEditor.document` and drop a response for any other document.

The word `ready` leaves the user interface. Each `Ready` row states what FsHttp.Studio found, which
is a stronger statement than the state of a process that the user did not start.

`looking for requests…` holds from the activation of a `.fsx` document until its first `blocks`
response. `Extension` resets to this state when the active document changes, and never after an
edit, so an edit updates the count in place.

### 6. Visibility: F# documents only

Show the item while the active editor's `languageId` is `fsharp`. Hide it in each other document, and
hide it when no editor has focus.

This matches `activationEvents: onLanguage:fsharp` and the CodeLens provider's own
`{ language = "fsharp" }` registration, so three parts of the extension agree on one audience. `.fs`
and `.fsi` stay in view, which is what makes `not an .fsx script` reachable. That string explains
ADR-0004's boundary at the moment the user meets it.

**This changes shipped v0.1 behavior on purpose.** The item stops being visible in each document. A
crashed companion is then visible only from an F# document, which is where the user can act on it.

An untitled F# buffer reads as `not an .fsx script`, because its name has no `.fsx` extension. The
lens behavior there is unchanged, and the status bar now explains it.

### 7. The count includes each block that #97 refuses

A script can hold three blocks of which #97 refuses two. The status bar reports `3 requests`.

The count equals the number of lenses on the screen, always, because #97 gives a refused block a
lens of its own. The status bar answers *did FsHttp.Studio find my requests*. The lens answers *can
this one run*. Keeping the two jobs apart is what stops the status bar from restating twelve refusal
families in one line.

### 8. The seam: both decisions move into `Protocol.fs`

`CodeLensProvider` and `Extension` hold VSCode interop and no test drives them. `Protocol.fs` states
at its head that it holds no Fable or VSCode interop, so `tests/host.Tests` drives it directly.

Add to `Protocol.fs`:

```fsharp
type ScriptView =
    | NoFSharpDocument
    | NotAScript                              // .fs or .fsi
    | ScriptPending                           // .fsx, no blocks response yet
    | Script of blocks: int * parseFailed: bool

val statusText : State -> ScriptView -> string option   // None hides the item
val noRequestsLensTitle : ScriptView -> string option   // Some for Script(0, true) only
```

`statusText` returns an option, so one function holds Decision 6's visibility rule and Decision 5's
table together, and one test suite asserts both.

**Move `State` and `statusText` from `Companion.fs` into `Protocol.fs`.** The move is forced and not
a preference: `Protocol.fs` compiles before `Companion.fs` (`Extension.fsproj:11-12`), so it cannot
refer to `Companion.State` where that type stands today. `Companion.fs` then opens `Protocol` and
loses two definitions. Each call site of `Companion.statusText` and `Companion.State` follows.

### 9. `Vscode.fs` gains four bindings

The hand-rolled interop covers only what the project uses. This spec needs four more members:

| Member | Reason |
|---|---|
| `StatusBarItem.hide: unit -> unit` | Decision 6 hides the item. |
| `TextDocument.languageId: string` | Decision 6 tests for `fsharp`. `fileName` cannot identify an untitled buffer. |
| `TextEditor` with `document: TextDocument` | Decision 5 compares the active document. |
| `IWindow.activeTextEditor: TextEditor option` and `IWindow.onDidChangeActiveTextEditor: (TextEditor option -> unit) -> Disposable` | The status bar reacts to a document switch. `provideCodeLenses` never fires for a document that is not F#, so it cannot drive the hide. |

Register the listener's `Disposable` in `context.subscriptions`, as each other subscription is.

### 10. The strings

| String | Where |
|---|---|
| `⊘ No requests found: this script has a syntax error` | The lens title |
| `starting…` | Status bar, unchanged |
| `.NET SDK not found` | Status bar, unchanged |
| `companion stopped` | Status bar, unchanged |
| `not an .fsx script` | Status bar |
| `looking for requests…` | Status bar |
| `1 request` | Status bar |
| `{n} requests` | Status bar |
| `no requests found` | Status bar |
| `no requests found — syntax error` | Status bar |
| `{n} requests — a syntax error can hide others` | Status bar |

Rules that these strings hold, and that a later edit must hold:

- No contractions, active voice, and American spellings, per `AGENTS.md` and the controlled-language
  skill.
- The user-facing noun is **request**, as `▶ Run request` and #97 both use. The glossary word
  *block* stays in this spec and in the code.
- The status bar states what FsHttp.Studio found. It states no corrective action, because it has no
  room for one and the lens carries the one that exists.
- `ready` is not among them. Refer to Decision 5.

## Testing Decisions

### Seam 1: the companion locates and reports the flag

`tests/companion.Tests/BlockLocatorTests.fs` drives `BlockLocator` directly. Add:

1. **A clean script reports `ParseFailed = false`**, for a script with blocks and for a script with
   none.
2. **A script with damage above the blocks reports `ParseFailed = true` and no block.** This is the
   pair that Decision 1 triggers on.
3. **A script with damage below every block reports `ParseFailed = true` and each block.** This is
   the case that Decision 1 must **not** trigger on, and the probe's largest finding.
4. **A script with damage between two blocks reports `ParseFailed = true` and one block.** Partial
   loss, which Decision 5 routes to the status bar.

Take the fixtures from the probe, which holds a measured expectation for each one.

### Seam 2: the envelope

`tests/companion.Tests/RequestHandlerTests.fs` round-trips a `locate` request through JSON. Add:

5. **A `blocks` response for a clean source carries `parseFailed: false`.**
6. **A `blocks` response for a broken source carries `parseFailed: true`**, and still carries each
   range that recovery found.

### Seam 3: the host's rules

`tests/host.Tests` drives `Protocol`. Add:

7. **Each row of Decision 5's table maps to its text.** Eleven assertions, one for each row.
8. **`statusText` returns `None` for `NoFSharpDocument`**, whatever the companion state is. This is
   Decision 6's precedence.
9. **A companion state other than `Ready` outranks the script view.** Assert with a `Script` view
   that would otherwise report a count.
10. **`noRequestsLensTitle` returns `Some` for `Script(0, true)` only.** Assert `None` for
    `Script(0, false)`, `Script(2, true)`, `ScriptPending`, `NotAScript`, and `NoFSharpDocument`.
11. **One request reads `1 request`, and two read `2 requests`.**
12. **An absent `parseFailed` property decodes to `false`.** This is Decision 3's compatibility rule.

### What is not tested here

No test drives the status bar item, the CodeLens surface, or the active-editor listener. These are
interop, and ADR-0003's seam puts the testable logic in `Protocol`. Seams 1 and 3 hold each rule and
each string.

## Out of Scope

- **An editor diagnostic or a decoration on the syntax error.** `Protocol.fs:38-40` decided that
  Ionide owns the editor. A second squiggle on the same character is noise.
- **Reporting the parse error's message or position.** Ionide reports both. The probe also found the
  reported position unreliable: `let a = (` on line 1 reported at line 2, and each unclosed-brace
  case reported the line after the damage.
- **An in-editor signal on each script with no block.** Ruled out by the deciding ticket. Most `.fsx`
  files that a user opens are not FsHttp scripts, and a lens on each of them is worse than silence.
  The trigger holds to *failure to parse*, and never to *absence of blocks*.
- **A separate message for a builder with a name other than `http`.** Telling `myHttp { }` apart from
  a script with no computation expression needs an AST branch that matches each builder name. That
  case reports `no requests found`, which is true.
- **A click target on the lens.** A toast that repeats the title carries nothing new. Focus of the
  Problems panel assumes that Ionide is installed and reporting, and an empty panel would read as a
  lost error.
- **A tooltip on the status bar item.** The item has none today, and each string above fits its line.
- **Per-document history in the host** to detect partial loss exactly. Refer to Decision 1.
- **A count of the blocks that a broken parse lost.** FsHttp.Studio cannot know it. Refer to the
  probe's second finding.

## Further Notes

### Dependency: #97, and a correction to the deciding ticket

The deciding ticket recorded that this envelope change "is unrelated to the `ok` envelope changes and
can land first". The first half is true. **The second half is wrong**, and this spec corrects it.

**#97 grows the same `blocks` envelope**, with a per-range `refusal` string, and it also rewrites
`Companion.locate`'s decoder, `RequestHandler`'s `locate` arm, and `BlockLocator`'s return shape.
#96 moves `classify` into `BlockLocator` as well. The deciding ticket named this two-grower trap for
the `ok` envelope and missed the identical trap on the `blocks` envelope that it was describing.

There is no conflict in behavior, only a merge. **This spec lands after #97**, because #97 is the
larger spec and this one is the smallest of the seven. The small spec absorbs the merge. The real
chain is **#96 → #97 → this spec**, since #97 needs #96.

The two properties sit at different levels of one JSON object: `parseFailed` is a property of the
envelope, and `refusal` is a property of a range. Both follow #97's rule for an absent property.

Decision 7 assumes #97's refused lens. Before #97 lands, the count is simply the count of located
blocks, which is the same number.

### Provenance

The decisions come from earlier planning work on this feature, in a session that read the shipping source and ran a
probe against FCS.

The probe put 26 sources through the locator's exact parse and fold. It corrected the premise that
this feature was chosen on:

- **"The lenses vanish while the user types" is mostly false.** The common state, where a user adds
  a block at the end of a script, keeps each lens.
- **Partial loss is real and is not measurable.** Recovery kept the second block and lost the first
  in one case, so the position of the error does not predict the loss.

Those two findings produced Decision 1's trigger and Decision 5's last table row. Each other decision
follows from the shipping source: the compile order that forces Decision 8's move, the four missing
interop members in Decision 9, and the always-visible status bar item that Decision 6 changes.
