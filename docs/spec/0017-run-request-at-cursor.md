# Run request at cursor in VSCode, and the cursor rule for both Clients

Spec for v0.3 shared feature 2 of 3. VSCode gets the palette command "FsHttp.Studio: Run request at
cursor". The map [FsHttp.Studio v0.3: Neovim support and shared features](https://github.com/tw0po1nt/FsHttp.Studio/issues/239)
records the decisions, and the ticket
[How does Run request at cursor work in VSCode?](https://github.com/tw0po1nt/FsHttp.Studio/issues/257)
records the detail.

## Problem Statement

**A Run needs the mouse.** The Run request CodeLens is the only way to start a Run in VSCode. A user
who works from the keyboard must reach for the mouse at each Run. A user who comes from the REST
Client extension expects a key that sends the request at the cursor.

The Neovim client starts a Run from the cursor with `:FsHttp run` (spec 0015). Without this
command, the two Clients start a Run in two different ways.

## Solution

**VSCode gets the palette command "FsHttp.Studio: Run request at cursor".** It runs the innermost
block at the cursor. When the cursor is outside every block, a quick pick lists each located block.
The command has no default key, and the README shows a `keybindings.json` entry for `ctrl+alt+r`.

**One cursor rule binds both Clients.** This spec defines the rule, and `:FsHttp run` in spec 0015
uses it. One Golden fixture pins it.

The companion and the envelope do not change. No setting is added.

## User Stories

1. As a VSCode user, I want "FsHttp.Studio: Run request at cursor", so that I start a Run from the keyboard.
2. As a VSCode user, I want the command to run the innermost block that contains the cursor, so that a cursor in a nested block runs what I point at.
3. As a VSCode user with the cursor outside every block, I want a quick pick that lists each block, so that I can choose one.
4. As a VSCode user, I want the quick pick to show the refused blocks with their reason, so that I see the full script.
5. As a VSCode user who picks a refused block, I want the refusal toast, so that I learn the workaround.
6. As a VSCode user, I want a README entry for `ctrl+alt+r`, so that I can bind the key that REST Client uses.
7. As a VSCode user who runs the command while the companion starts, I want a progress notification with Cancel, so that I can wait or stop.
8. As a VSCode user in a file that is not a Script, I want an INFO toast that tells me what the command needs, so that I know why nothing ran.
9. As a VSCode user in a script with a syntax error that hides every block, I want the same sentence as the lens, so that I know what to fix.
10. As a VSCode user with a stopped companion, I want the stopped toast with its "Restart companion" button, so that I can recover.
11. As a VSCode user, I want the command in the palette only for a Script, so that the palette stays clean.
12. As a user of both editors, I want the same cursor rule and the same picker text in VSCode and Neovim, so that one habit works in both.
13. As the maintainer, I want one Golden fixture for the cursor rule, so that the two Clients cannot drift.

## Implementation Decisions

### 1. The command

- The id is `fshttpStudio.runRequestAtCursor`. The palette label is "FsHttp.Studio: Run request at
  cursor". It has no default key.
- `menus.commandPalette` shows it with this clause:

  ```
  editorLangId == fsharp && resourceExtname == .fsx
  ```

  This is the test that `isScriptFileName` applies to a lens. An untitled F# buffer gets no entry,
  and it gets no lens.
- A user keybinding with no `when` clause, or `executeCommand` from another extension, can still
  reach the command in a file that is not a Script.

### 2. Block ranges

- Each call locates the editor text again, maps the cursor to a range, and sends the run envelope
  with the same text. Thus the block index always matches the text that the companion parses.
- The ranges of the last locate in `CodeLensProvider` stay for the stopped lenses only.

### 3. The cursor rule (both Clients)

- A block contains the cursor when the cursor line is between the start line and the end line of
  the block range. The column has no effect.
- The innermost block is the block with the latest start. A cursor in a block inside another block
  targets the inner block, which gets the `insideAnotherRequest` refusal.
- The command uses the primary cursor.
- The line `let getSnorlax () =` above a block that starts on the next line is outside the block.
  A cursor on that line opens the quick pick.
- The rule is a pure function in the host. host.Tests writes a Golden fixture of cursor cases, and
  the Lua core suite of spec 0015 reads it.

### 4. The outcome for each state

| State | Result |
|---|---|
| No active editor, or an editor that is not a Script | INFO toast: "Open an F# script (.fsx) to run the request at the cursor." |
| Companion stopped | The stopped toast that a stopped lens shows, with the "Restart companion" button of spec 0018. The command maps no cursor and opens no quick pick. |
| .NET SDK not found | The SDK toast that activation shows, with the "Get the .NET SDK" button. The command maps no cursor and opens no quick pick. |
| Companion starting | The wait of decision 6. |
| A Parse failure and no block | WARN toast: "No requests found: this script has a syntax error." |
| No block and no Parse failure | INFO toast: "This script has no request. Write an http { } block to run one." |
| Cursor in a runnable block | A Run of that block. The Response viewer opens beside the editor, as for a lens click. |
| Cursor in a refused block | The WARN toast of the refused lens. No Run starts, and the Response viewer stays closed. |
| Cursor outside every block | The quick pick of decision 5. |

- A script with a Parse failure that still has blocks follows the cursor rule.
- The two no-request sentences live in `Refusals.fs`. `:FsHttp run` uses the same sentences at the
  same levels.

### 5. The quick pick

- The quick pick lists every located block in source order, refused blocks too.
- Each label shows the glyph of the lens title, the line number, and the first source line of the
  block.
- A refused item shows the title of its refusal in the detail row.
- A pick on a runnable block starts a Run. A pick on a refused block shows the WARN toast of the
  refused lens, and no Run starts.
- The text matches the picker of `:FsHttp run`.

```
▶ 4: let getSnorlax () = http {
▶ 12: let postBerry = http {
⊘ 21: http {
      Cannot run: inside a loop
▶ 30: let deep = http {
```

### 6. The wait while the companion starts

- A progress notification shows "Waiting for the FsHttp.Studio companion to start", with a Cancel
  button.
- The command records the editor text and the cursor.
- When the companion is ready, the notification closes. The recorded text and cursor then go
  through the normal rule, and the quick pick can open.
- One call at most waits. A new call replaces the recorded text and cursor.
- Cancel discards the wait, and no Run starts.
- If the companion stops, or the .NET SDK is not found, the command discards the wait and shows the
  toast for that state.

### 7. README

A short section, "Run the request at the cursor", follows the lens usage. It shows this
`keybindings.json` entry:

```json
{
  "key": "ctrl+alt+r",
  "command": "fshttpStudio.runRequestAtCursor",
  "when": "editorTextFocus && editorLangId == fsharp && resourceExtname == .fsx"
}
```

The REST Client extension uses `ctrl+alt+r` for its Send Request command.

## Testing Decisions

### What makes a good test

A Check calls the command through the palette, as a user does. It asserts the Response viewer, the
toast, or the quick pick items. It does not assert how the command maps the cursor.

### host.Tests

- The cursor rule against each case of its Golden fixture: a cursor on the first line, the last
  line, and a middle line of a block, a cursor in a nested block, a cursor on the line above a
  block, and a cursor outside every block.

### UI suite

1. The cursor is in a runnable block of `core-path.fsx`. The Response viewer shows the result.
2. The cursor is in the loop block of `loop-lens.fsx`. The `loopBody` toast shows, and no Response
   viewer opens.
3. The cursor is outside every block. The quick pick lists each block with the labels above, and a
   pick starts a Run.
4. `no-requests-empty.fsx` is open. The INFO toast shows.
5. The companion is stopped. The stopped toast shows.

The wait while the companion starts gets no Check. `release-gate.md` names that gap.

### Prior art

- The loop lens Check and the core path Check of the UI suite.

## Out of Scope

- A default key for the command.
- A Run of each block in a script, or of a selection.
- A palette entry for an untitled F# buffer.

## Further Notes

- This spec reverses spec 0003 Decision 9, which put a palette command for the block at the cursor
  out of scope for v0.2.
- The "Restart companion" button on the stopped toast comes from spec 0018. Until 0018 ships, the
  stopped toast has no button.
- No ADR conflict. ADR-0003 states "no companion, no runnable lenses", and the command starts no
  Run without a ready companion.
