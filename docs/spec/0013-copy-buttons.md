# Copy buttons for the request, the response headers, and the response body

Spec for v0.2 feature 2 of 3: the response viewer gets one copy button for the request, one for
the response headers, and one for the response body.

## Problem Statement

**The response viewer shows text that the user cannot take out of it.** A user reads a JSON
response, a header block, or the request that went out, and then wants to put it in an issue, a
chat message, or a test fixture. The panel offers no way to do this.

The workaround is a manual selection with the mouse, and the panel defeats it in three ways:

1. **The JSON tree is not JSON.** `renderJson` (`Renderer.fs:199`) builds a tree of `<details>`,
   `<summary>`, and `<span>` elements. A selection across that tree copies the disclosure markers
   and the layout whitespace. The result does not parse.
2. **The header rows are a CSS grid.** `.header-row` (`ResponseViewer.fs:127`) is a two-column
   grid. A selection over it produces the name and the value separated by the newlines that the
   grid layout puts there, and not by a colon.
3. **A large body is a scroll.** A user must drag through a scrolling region to select a body that
   the panel already holds complete in memory.

**Nothing at all can be copied from the Request section**, which #99 adds. That section is the
answer to "did it send what I wrote", and the most common next action after reading it is to show
it to another person.

## Solution

**Each of the three sections gets one copy button.** The button sits in the top right corner of
its section. A click puts the section's content on the clipboard, and the button reports the
result in its own label.

The payload comes from **one pure function in the renderer core**, and not from the rendered DOM.
This is what makes the JSON case correct: the copy never reads the tree, so it cannot copy the
tree's disclosure markers.

The webview reaches `navigator.clipboard.writeText` directly. The panel gains its first event
listener. It does not gain an outbound message channel, a new protocol tag, or a new command.

## User Stories

1. As a script author, I want to copy a JSON response body and paste valid JSON, so that I can put
   it in a test fixture without repairing it by hand.
2. As a script author, I want to copy the response headers as `Name: value` lines, so that a paste
   into an issue is readable.
3. As a script author, I want to copy the request that went out, so that I can show another person
   exactly what my script sent.
4. As a script author, I want the copied request to name its method and URL, so that the paste says
   which request it describes.
5. As a script author, I want the copied response headers to name the status, so that the paste
   says which response it describes.
6. As a script author who copied a request whose body was too large to capture, I want the reason
   in the paste, so that I do not read a missing body as "no body was sent".
7. As a script author, I want to see that the copy succeeded, so that I do not click the button a
   second time.
8. As a script author on a platform that refuses clipboard access, I want to be told, so that I do
   not paste the previous contents of my clipboard by mistake.
9. As a maintainer, I want the copied text decided by a pure function, so that the Seam-B suite
   proves what the clipboard receives.

## Implementation Decisions

### 1. The payload comes from one pure function in the renderer core

Add to `Renderer.Core`:

```fsharp
/// The text that a copy button puts on the clipboard, for one copy key. `None` means that there
/// is nothing to copy, and the renderer then omits the button.
let copyText (env: ResponseEnvelope) (key: string) : string option
```

The renderer emits a button that carries the **key**, and not the payload:

```fsharp
el "button" [ "class", "copy-button"; "type", "button"; "data-copy", "response-body" ]
           [ Node.Text "Copy" ]
```

`Webview.Dom` attaches **one** delegated `click` listener to the panel root. The listener finds the
nearest ancestor that matches `[data-copy]`, reads the key, calls `copyText`, and writes the result
to the clipboard.

There are three keys, and no others:

| Key | Section |
| --- | --- |
| `request` | The Request section that #99 adds |
| `response-headers` | The response headers section |
| `response-body` | The response body |

**Why the key and not the payload.** An attribute that holds the payload puts a full copy of the
body in the DOM. A 1 MB response body then costs 1 MB of markup, and the header block appears
twice. The key costs a few bytes, and it keeps one authority for the payload.

**Why a pure function and not a handler on `Node`.** `Node` is a value, and the Seam-B suite asserts
its shape. A handler of type `unit -> unit` is opaque to a test, so the two rules that this spec
exists to get right become untestable. A pure function keeps them in the suite.

### 2. The button is a sibling of its section, inside a positioned shell

Each section is wrapped in a shell element with `position: relative`. The button is a child of the
shell, and a sibling of the section. The button is **never** a descendant of the section.

```html
<div class="section-shell">
  <button class="copy-button" type="button" data-copy="response-headers">Copy</button>
  <details class="headers">
    <summary class="headers-summary">Headers (12)</summary>
    ...
  </details>
</div>
```

This placement answers two hazards at the same time.

**Hazard 1: a button inside a `<summary>` toggles the section.** A click on a descendant of
`<summary>` opens or closes the `<details>`. A button placed there must call `preventDefault` on
every click, or each copy also collapses the section the user was reading. A button outside the
`<details>` cannot toggle it, so the rule is not needed.

**Hazard 2: a positioned child of a closed `<details>` is not painted.** This was measured, and it
is the reason for the shell.

| Placement | `<details>` state | Painted |
| --- | --- | --- |
| Child, after `<summary>` | closed | **No** |
| Child, before `<summary>` | closed | **No** |
| Sibling, inside a shell | closed | Yes |
| Child, after `<summary>` | open | Yes |

DOM order does not change the result. In both failing cases the browser still reports a 29x23 box,
with `visibility: visible` and `content-visibility: visible`, so no measurement in code detects the
defect. Only a screenshot shows it.

This matters because **both sections are collapsed by default**. `renderHeaders`
(`Renderer.fs:242`) emits `<details class="headers">` with no `open` attribute, and #99 makes the
Request section collapsed by default. A button placed inside either one is invisible until the user
opens the section.

`.response-body` (`Renderer.fs:263`) is not a `<details>`, but it sets `overflow: auto`
(`ResponseViewer.fs:144`). A positioned child of it scrolls away with the content. It therefore
needs the same shell, and the shell does not scroll.

All three sections use one shell, one CSS rule set, and one placement rule.

### 3. The response body payload reads the bytes, and never the Content-Type

```fsharp
let private bodyCopyText (bytes: byte[]) : string =
    if looksBinary bytes then hexDump bytes else decodeText bytes
```

`looksBinary` (`Renderer.fs:99`), `hexDump` (`Renderer.fs:127`), and `decodeText`
(`Renderer.fs:92`) are all private members of the same module, so this function needs no visibility
change.

`renderBody` dispatches on the Content-Type, and this function does not. That is deliberate. A
second dispatch on the Content-Type is a second thing to keep in step with the first, and this rule
gives the correct answer on all five render paths without one:

| Render path | Bytes | Copied text |
| --- | --- | --- |
| JSON tree | text | **The raw body bytes**, and not the tree |
| Sandboxed iframe | text | The page source |
| `image/svg+xml` | text | The SVG source |
| `image/png` | binary | The hex dump |
| Text or hex fallback | either | The same text or hex that is shown |

The first row is the first trap that the ticket named. The rule answers it by construction, because
the copy never reads the render.

The request body uses the same function. #99 renders the request body through the same content
dispatch, minus the image and HTML branches, so the two stay consistent.

### 4. A binary body copies the truncated dump, exactly as shown

`hexDump` caps at 256 bytes and appends `… (N more bytes)`. The copy uses it unchanged, so the
clipboard receives the cap and the note.

This is the second trap that the ticket named, and the cap was not in view when the ticket was
written. The decision stands after inspection, for two reasons.

**The note travels with the paste.** A user reads `… (2417283 more bytes)` on screen and pastes
that same line, so nothing is concealed.

**A full dump is not a paste.** A 4 MB body produces about 262,000 lines, which is more than a
person puts in an issue or a chat message. The clipboard is the wrong carrier for a large binary
body. The feature-cap ticket already rejected save-to-file, and recorded that gap as a v0.3 item.
This spec does not reopen it.

A text body copies in full, because the panel shows it in full. The rule is "what you see", and the
render is what is asymmetric.

### 5. One serialization function, used by two callers

```fsharp
/// A message-shaped block: a first line, one `Name: value` line for each header, and an optional
/// body after a blank line.
let private messageText (firstLine: string) (headers: (string * string) list) (body: string option)
    : string
```

The header list is already deduplicated, and multi-valued headers are already joined with `", "`,
by the code that builds the envelope. This function does not repeat that work.

**The request** copies the `RequestView` record that #99 introduces:

```
POST https://api.example.com/items
Content-Type: application/json
Authorization: Bearer eyJ…
Accept-Encoding: gzip, deflate

{"name":"fs","tags":["a"]}
```

The method and the URL come from `env.Request`, which is where #99 put them. The button therefore
copies the **record**, and not the rendered region. The status line draws the method and the URL,
and the Request section draws the headers and the body, but there is one request, and one paste
that describes it.

**The response headers** copy the status and the headers:

```
200 OK
Content-Type: application/json; charset=utf-8
Date: Sat, 02 Aug 2026 11:04:22 GMT
Server: nginx
```

The two response buttons cover the whole response between them, and each paste says what it is.

### 6. `copyText` in full

```fsharp
let copyText (env: ResponseEnvelope) (key: string) : string option =
    match key with
    | "request" ->
        let body =
            match env.Request.Body with
            | NoBody -> None
            | Captured bytes -> Some(bodyCopyText bytes)
            | NotCaptured reason -> Some reason

        Some(messageText (sprintf "%s %s" env.Request.Method env.Request.Url) env.Request.Headers body)
    | "response-headers" -> Some(messageText (sprintf "%d %s" env.Status env.Reason) env.Headers None)
    | "response-body" -> if env.Body.Length = 0 then None else Some(bodyCopyText env.Body)
    | _ -> None
```

**`NotCaptured` puts its reason on the clipboard, in the body's place.** A paste that drops a body
that the script did send is the exact failure that #99's three-state body exists to prevent, and a
copy must not reintroduce it:

```
POST https://api.example.com/upload
Content-Type: multipart/form-data; boundary=…

Body not captured: 5.2 MB exceeds the 1 MB cap
```

### 7. `None` removes the button

```fsharp
let private copyButton (env: ResponseEnvelope) (key: string) : Node list =
    match copyText env key with
    | Some _ -> [ el "button" [ "class", "copy-button"; "type", "button"; "data-copy", key ] [ Node.Text "Copy" ] ]
    | None -> []
```

The button exists when there is something to copy, and not otherwise. Only one case produces
`None`: a response body of zero bytes, such as a `204 No Content`.

The other two are always `Some`. The response headers always carry at least the status line, and
the request always carries at least its request line, so neither button disappears when its list is
empty.

The alternative, a button that copies an empty string, reports `Copied` while it clears the user's
clipboard. That is worse than an absent button.

### 8. The button reports the result in its own label

```fsharp
let private flash (button: HTMLElement) (text: string) =
    let original = button.textContent
    button.textContent <- text
    window.setTimeout ((fun () -> button.textContent <- original), 1200) |> ignore
```

A success shows `Copied`. A rejected promise shows `Copy failed`. Both revert after 1200 ms.

The button carries `aria-live="polite"`, so a screen reader announces the change.

**The failure path is not decoration.** `navigator.clipboard.writeText` returns a promise that
rejects in at least two cases that this extension meets:

- **VSCode for the Web, in Firefox.** The webview shell grants clipboard access only when the
  browser is not Firefox. Decision 11 records the measurement.
- **An unfocused document.** Chromium rejects with `NotAllowedError` when the document does not
  have focus.

A silent failure leaves the user with a stale clipboard and no signal. They then paste the previous
contents and do not know why.

**Why not a VSCode notification.** A notification needs `acquireVsCodeApi`, an outbound message from
the webview, a receiver on the panel, and a new protocol tag. That is a whole channel for a label
that the button can change itself. A notification for each copy is also noisier than a label.

`navigator.clipboard` is not in Fable's `Browser.Navigator` bindings. Reach it through
`emitJsExpr`, in the same style as `ResponseViewer.nonce` (`ResponseViewer.fs:16`).

### 9. The button shows the word `Copy`

The button is a `<button>` element whose text is `Copy`. The text is the accessible name, so the
button needs no `aria-label`.

**A codicon is not available.** The panel's CSP is `default-src 'none'`, with `img-src`,
`style-src`, and `script-src` only (`ResponseViewer.fs:200`). It declares no `font-src`, and it
permits no external stylesheet, so the VSCode codicon font cannot load. Adding `font-src` to widen
the CSP for a decorative glyph is not worth the change.

**An inline SVG icon is not available either.** `Dom.mountNode` (`Dom.fs:11`) calls
`document.createElement`, which produces a dead `HTMLUnknownElement` for `<svg>`. An SVG icon
requires `createElementNS` and namespace inheritance for the child elements, which is a change to
Seam B's mount for decoration.

A text label also fits Decision 8 without a second visual vocabulary. `Copy`, `Copied`, and
`Copy failed` are three words. A glyph needs a second set of glyphs to say the same thing.

### 10. The listener lives in `Webview.Dom`, and reads the current envelope

`Dom.renderInto` (`Dom.fs:30`) receives the envelope, and it is the one function that mounts a
render. Store the envelope in a module-level mutable, and attach the listener once.

```fsharp
let mutable private current: ResponseEnvelope option = None
let mutable private attached = false

let renderInto (parent: HTMLElement) (env: ResponseEnvelope) : unit =
    current <- Some env

    if not attached then
        parent.addEventListener ("click", handleClick)
        attached <- true

    parent.innerHTML <- ""
    parent.appendChild (mount (render env)) |> ignore
```

`parent.innerHTML <- ""` replaces the children, and the listener is on `parent`, so the listener
survives each render. One listener therefore serves every Run.

`Dom.fs` states that its mount glue goes to the manual smoke, and not to an automated suite. The
listener follows that rule. The payload that the listener sends is in the Seam-B suite, because
Decision 1 put it in a pure function.

### 11. Two measured facts that the reader needs

**VSCode grants the webview clipboard access, and the ticket's premise holds.** Measured in
VSCodium's own webview shell,
`out/vs/workbench/contrib/webview/browser/pre/index.html`, at the point where it builds the inner
frame:

```js
const allowRules = ['cross-origin-isolated;', 'autoplay;', 'local-network-access;'];
if (!isFirefox && options.allowScripts) {
    allowRules.push('clipboard-read;', 'clipboard-write;');
}
newFrame.setAttribute('allow', allowRules.join(' '));
```

`ResponseViewer.showBeside` passes `enableScripts = true` (`ResponseViewer.fs:224`), so
`options.allowScripts` is true and the grant applies. The frame's sandbox includes
`allow-same-origin`, so the origin is not opaque and the asynchronous clipboard API is usable.

**VSCode for the Web in Firefox gets no grant**, by the same line. The copy fails there, and
Decision 8 shows `Copy failed`. This spec accepts that outcome and does not work around it. A
`document.execCommand("copy")` fallback is out of scope, below.

### 12. Styles to add

Add to `responseStyles` (`ResponseViewer.fs:30`):

```css
.section-shell { position: relative; }
.copy-button {
  position: absolute;
  top: 4px;
  right: 6px;
  padding: 2px 8px;
  font-family: var(--vscode-font-family);
  font-size: 0.82em;
  border: 1px solid var(--vscode-button-border, transparent);
  border-radius: 4px;
  background: var(--vscode-button-secondaryBackground, rgba(128,128,128,0.2));
  color: var(--vscode-button-secondaryForeground, var(--vscode-foreground));
  opacity: 0.55;
  cursor: pointer;
}
.copy-button:hover,
.copy-button:focus-visible {
  opacity: 1;
  background: var(--vscode-button-secondaryHoverBackground, rgba(128,128,128,0.3));
}
```

The shell replaces the sections' own `margin-bottom` rules, so that the spacing does not double.
Move `margin-bottom: 12px` from `.headers`, from `.request`, and from the body container onto
`.section-shell`.

The button stays at 55% opacity until the pointer or the keyboard reaches it. It is always present,
and never hover-only, so a touch user and a keyboard user both reach it.

## Testing Decisions

### Seam B: the renderer core, in the Expecto suite

The payload is a pure function of a canned envelope, so each rule below is one assertion in
`tests/renderer.Tests/RendererTests.fs`.

1. **A JSON body copies the raw bytes, and not the tree.** `copyText env "response-body"` for an
   `application/json` body equals the body decoded as UTF-8. The result parses. It contains no
   `▸` and no `Object(`. This is the first named trap.
2. **A binary body copies the dump as shown.** `copyText env "response-body"` for a body of more
   than 256 bytes with NUL bytes equals the rendered `hexDump`, and ends with the
   `… (N more bytes)` line. This is the second named trap.
3. An `image/svg+xml` body copies the SVG source, and not a hex dump.
4. A `text/html` body copies the page source.
5. A response body of zero bytes yields `None`, and the rendered tree contains no
   `[data-copy="response-body"]` button.
6. `copyText env "response-headers"` starts with the status line, and then has one `Name: value`
   line for each header, in order.
7. A response with no headers still yields `Some`, and the payload is the status line alone.
8. `copyText env "request"` starts with the method and the URL, and separates the headers from the
   body with one blank line.
9. A `NoBody` request copies the request line and the headers, and ends after the last header.
10. A `NotCaptured` request copies the reason in the body's position.
11. A full render contains exactly **three** `.copy-button` nodes, and their `data-copy` values are
    `request`, `response-headers`, and `response-body`.
12. A full render of a `204 No Content` contains exactly **two** `.copy-button` nodes.
13. **No `.copy-button` node is a descendant of a `<details>` element or of a `<summary>` element.**
    This is the regression test for Decision 2. It fails if anybody moves a button inside a
    section, which is the change that makes two of the three buttons invisible.

### The JavaScript runtime smoke

Add one `copyText` check to `Webview.Smoke.run`.

`Smoke.fs` exists because a `StringBuilder` in the core passed every .NET test and then failed to
bundle under Fable. `copyText` is new core code on exactly that path, so it needs the same guard.
One JSON payload check is enough, because it exercises `decodeText`, `looksBinary`, and the string
concatenation together.

### The manual smoke

`Dom.fs` records that its mount glue goes to the manual smoke. These four checks go in the PR
description:

1. A click on each of the three buttons puts the expected text on the clipboard.
2. The label reads `Copied` and reverts after about one second.
3. **A click on a copy button does not open or close its section.**
4. **Both header sections show their copy button while collapsed.** This is the defect that
   Decision 2 exists to prevent, and no automated check sees it.

## Out of Scope

- **A copy button on the error render.** `Main.fs:74` handles `"error"` with
  `root.textContent <- message`, and it does not use the renderer at all. A compile error is a
  paste-worthy string, and this is the strongest candidate that this spec turns down. It is out of
  scope for two reasons. It is a fourth target, and the feature-cap ticket priced this feature at
  three. It also needs the error render moved into the renderer core, so that `copyText` stays the
  one payload authority, and #95, #97, and #98 are all rewriting what lands on that path. Revisit
  in v0.3, after those three land.
- **Copy as curl.** A v0.3 item by the feature-cap ticket's decision, and by #99's own out-of-scope
  list.
- **Save the response to a file.** Rejected by the feature-cap ticket. Decision 4 records the gap
  that the rejection leaves.
- **A `document.execCommand("copy")` fallback** for VSCode for the Web in Firefox. Decision 11
  measured the one platform that refuses, it is not a target of this extension, and Decision 8
  tells the user what happened.
- **A copy button on the status line**, for the URL alone. Decision 5 puts the URL at the head of
  the request paste, and nobody has asked for it on its own.
- **Copying a selection**, or any change to how the panel handles a manual selection. The problem
  statement describes what a manual selection produces today. This spec adds a button beside it,
  and does not change it.
- **Copying an image body as an image.** The clipboard carries text here. Decision 3 gives an SVG
  its source and a PNG its hex dump.

## Further Notes

### Sequencing

This spec is written against the code **after #99**, and it is the last link in v0.2's longest
chain:

**#96 → #98 → #99 → this spec.**

- **#99** introduces `RequestView`, the Request section, and the three-state request body.
  Decisions 5, 6, and 7 all read that record. The Request section does not exist before it.
- #99 in turn needs #98's `TotalMs` on the envelope, and #98 needs #96's invocation-time
  `Config.update`.

This spec adds no field to any envelope, and no tag to the protocol. It is the only one of v0.2's
seven that changes neither the companion nor the host.

### What this feature does not change

- The companion is untouched.
- `Protocol.fs` is untouched, and no envelope gains a field.
- `ResponseViewer.fs` gains CSS only. Its CSP, its nonce, and its HTML are unchanged.
- `RunCommand.fs` is untouched.

### Terminology

`CONTEXT.md` needs no new term. A copy button is an affordance on the response viewer, and the
response viewer is already defined. No ADR is needed, because this decides no architecture. The
one architectural question that came up, whether `Node` should carry event handlers, was answered
**no** in Decision 1, and the reason is the one that `Renderer.fs` already states in its header
comment.
