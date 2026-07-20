// The renderer core (Seam B): a presentation-shell-agnostic pure
// function of a response envelope that yields the DOM to show. It produces an immutable `Node`
// tree rather than touching a real DOM, so it compiles to .NET for the Expecto suite (no VSCode,
// no browser) and to JS for the webview, where `Dom.mount` materialises the same tree into real
// elements. Keeping the tree a value is what makes "assert the DOM shape" a black-box test of a
// pure function.
module Renderer.Core

open System
open System.Text

/// An immutable description of a DOM node. `Element` carries a tag, a flat attribute list
/// (`class`, `src`, `srcdoc`, the boolean `open`, …), and children; `Text` is a text node. The
/// webview's `Dom.mount` is the only thing that turns this into real `HTMLElement`s.
[<RequireQualifiedAccess>]
type Node =
    | Element of tag: string * attrs: (string * string) list * children: Node list
    | Text of string

/// A response ready to render. The companion's `ok` envelope supplies `Status`, `Reason`,
/// `Headers`, `ContentType`, and the body (as bytes); `Method`, `Url`, and `ElapsedMs` are the
/// request context the host pairs with it for the status line. The renderer is a pure function of
/// this record — the Seam-B suite drives canned instances.
type ResponseEnvelope =
    { Method: string
      Url: string
      Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      Body: byte[]
      ElapsedMs: float }

// --- small Node helpers -------------------------------------------------------------------

let private el tag attrs children = Node.Element(tag, attrs, children)

let private span cls text =
    Node.Element("span", [ "class", cls ], [ Node.Text text ])

/// Re-encodes a decoded string as a JSON string literal for display. The parser resolves escapes
/// into real characters, so rendering a value or key verbatim between quotes would show an
/// unescaped `"` or `\` — dishonest JSON. Re-escaping restores a literal that reads as JSON.
/// (No StringBuilder — see `Json.fs` for why — so the chars are concatenated.)
let private jsonQuote (s: string) : string =
    let escaped =
        s
        |> Seq.map (fun ch ->
            match ch with
            | '"' -> "\\\""
            | '\\' -> "\\\\"
            | '\n' -> "\\n"
            | '\r' -> "\\r"
            | '\t' -> "\\t"
            | '\b' -> "\\b"
            | '\f' -> "\\f"
            | c when c < ' ' -> sprintf "\\u%04x" (int c)
            | c -> string c)
        |> String.concat ""

    "\"" + escaped + "\""

// --- formatting ---------------------------------------------------------------------------

/// Bins the status into the CSS class the shell colors on (`status-2xx` … `status-5xx`). The
/// class — not a hard-coded color — is what the core commits to, so the shell owns the palette.
let statusClass (status: int) : string =
    if status >= 200 && status < 300 then "status-2xx"
    elif status >= 300 && status < 400 then "status-3xx"
    elif status >= 400 && status < 500 then "status-4xx"
    elif status >= 500 && status < 600 then "status-5xx"
    else "status-other"

/// A compact human-readable byte size for the status line (`842 B`, `1.4 KB`, `2.0 MB`).
let humanSize (bytes: int) : string =
    let b = float bytes

    if bytes < 1024 then sprintf "%d B" bytes
    elif b < 1024.0 * 1024.0 then sprintf "%.1f KB" (b / 1024.0)
    else sprintf "%.1f MB" (b / 1024.0 / 1024.0)

/// Normalises a Content-Type for dispatch: lower-cased and stripped of parameters (`; charset=…`).
let normalizeContentType (contentType: string) : string =
    let ct = if isNull (box contentType) then "" else contentType
    let semi = ct.IndexOf(';')
    let bare = if semi >= 0 then ct.Substring(0, semi) else ct
    bare.Trim().ToLowerInvariant()

let private decodeText (bytes: byte[]) : string = Encoding.UTF8.GetString bytes

let private toBase64 (bytes: byte[]) : string = Convert.ToBase64String bytes

/// A body "looks binary" when it carries a NUL byte or a heavy fraction of non-text control
/// bytes — the signal that decoding it as text would be noise, so the hex fallback wins. Tab,
/// newline, and carriage return are excluded so ordinary text is never mistaken for binary.
let private looksBinary (bytes: byte[]) : bool =
    if bytes.Length = 0 then
        false
    else
        let isControl b =
            b < 32uy && b <> 9uy && b <> 10uy && b <> 13uy

        let hasNul = bytes |> Array.exists (fun b -> b = 0uy)

        let controlCount = bytes |> Array.filter isControl |> Array.length |> float

        hasNul || controlCount / float bytes.Length > 0.30

// --- Content-Type dispatch ----------------------------------------------------------------

let private renderImage (env: ResponseEnvelope) : Node =
    let mediaType = normalizeContentType env.ContentType
    let src = sprintf "data:%s;base64,%s" mediaType (toBase64 env.Body)
    el "img" [ "class", "response-image"; "src", src; "alt", "response image" ] []

let private renderHtml (bytes: byte[]) : Node =
    // A sandboxed iframe (empty `sandbox` = no scripts, no same-origin) renders the page as a
    // browser would while denying the response body any privileges over the panel.
    el "iframe" [ "class", "response-html"; "sandbox", ""; "srcdoc", decodeText bytes ] []

let private renderText (bytes: byte[]) : Node =
    el "pre" [ "class", "response-text" ] [ Node.Text(decodeText bytes) ]

let private hexDump (bytes: byte[]) : string =
    let maxBytes = min bytes.Length 256
    let lines = ResizeArray<string>()
    let mutable offset = 0

    while offset < maxBytes do
        let lineLen = min 16 (maxBytes - offset)

        let hex =
            [ for j in 0..15 ->
                  if j < lineLen then
                      sprintf "%02x " (int bytes.[offset + j])
                  else
                      "   " ]
            |> String.concat ""

        let ascii =
            [ for j in 0 .. lineLen - 1 ->
                  let b = bytes.[offset + j]
                  if b >= 32uy && b < 127uy then string (char b) else "." ]
            |> String.concat ""

        lines.Add(sprintf "%08x  %s %s" offset hex ascii)
        offset <- offset + 16

    let dumped = lines |> String.concat "\n"

    if bytes.Length > maxBytes then
        dumped + sprintf "\n… (%d more bytes)" (bytes.Length - maxBytes)
    else
        dumped

let private renderBinary (bytes: byte[]) : Node =
    el
        "div"
        [ "class", "response-binary" ]
        [ el "div" [ "class", "binary-note" ] [ Node.Text(sprintf "Binary response — %s" (humanSize bytes.Length)) ]
          el "pre" [ "class", "hex-dump" ] [ Node.Text(hexDump bytes) ] ]

/// The text/unknown/XML fallback: readable monospace when the bytes decode as text, otherwise the
/// non-crashing size/hex view. This is where an undecodable binary body lands without throwing.
let private renderTextOrBinary (bytes: byte[]) : Node =
    if looksBinary bytes then
        renderBinary bytes
    else
        renderText bytes

/// A collapsible container arm shared by the array and object renders: an open `<details>` whose
/// `<summary>` carries the count label, above the rendered entries.
let private jsonCollapsible (kindClass: string) (summaryText: string) (children: Node list) : Node =
    el
        "details"
        [ "class", sprintf "json-node %s" kindClass; "open", "" ]
        (el "summary" [ "class", "json-summary" ] [ Node.Text summaryText ] :: children)

let rec private renderJsonValue (value: Json.JsonValue) : Node =
    match value with
    | Json.Null -> span "json-null" "null"
    | Json.Bool b -> span "json-bool" (if b then "true" else "false")
    | Json.Number n -> span "json-number" n
    | Json.String s -> span "json-string" (jsonQuote s)
    | Json.Array items ->
        items
        |> List.map (fun item -> el "div" [ "class", "json-entry" ] [ renderJsonValue item ])
        |> jsonCollapsible "json-array" (sprintf "Array(%d)" (List.length items))
    | Json.Object members ->
        members
        |> List.map (fun (key, v) ->
            el "div" [ "class", "json-entry" ] [ span "json-key" (jsonQuote key); Node.Text ": "; renderJsonValue v ])
        |> jsonCollapsible "json-object" (sprintf "Object(%d)" (List.length members))

let private renderJson (bytes: byte[]) : Node =
    match Json.tryParse (decodeText bytes) with
    | Some value -> el "div" [ "class", "response-json" ] [ renderJsonValue value ]
    | None ->
        // An `application/json` Content-Type on a malformed body renders honestly as text rather
        // than crashing the panel.
        renderTextOrBinary bytes

let private isJson (ct: string) =
    ct = "application/json" || ct = "text/json" || ct.EndsWith("+json")

let private isHtml (ct: string) =
    ct = "text/html" || ct = "application/xhtml+xml"

/// Dispatches a response body to rendered DOM on its Content-Type: images to an `<img>` with a
/// `data:` URI, JSON to a collapsible highlighted tree, HTML to a sandboxed iframe, and everything
/// else (plain text, XML, unknown) to the monospace/hex fallback. Status is deliberately *not*
/// consulted here — a non-2xx HTTP error response renders its body exactly like any other; only
/// the status line shows the code.
let renderBody (env: ResponseEnvelope) : Node =
    let ct = normalizeContentType env.ContentType

    if ct.StartsWith("image/") then renderImage env
    elif isJson ct then renderJson env.Body
    elif isHtml ct then renderHtml env.Body
    else renderTextOrBinary env.Body

// --- status line + headers ----------------------------------------------------------------

let private renderStatusLine (env: ResponseEnvelope) : Node =
    el
        "div"
        [ "class", "status-line" ]
        [ span "status-method" env.Method
          span "status-url" env.Url
          el
              "span"
              [ "class", sprintf "status-code %s" (statusClass env.Status) ]
              [ Node.Text(sprintf "%d %s" env.Status env.Reason) ]
          span "status-time" (sprintf "%d ms" (int (Math.Round env.ElapsedMs)))
          span "status-size" (humanSize env.Body.Length) ]

let private renderHeaders (headers: (string * string) list) : Node =
    let rows =
        headers
        |> List.map (fun (name, value) ->
            el "div" [ "class", "header-row" ] [ span "header-name" name; span "header-value" value ])

    el
        "details"
        [ "class", "headers" ]
        (el "summary" [ "class", "headers-summary" ] [ Node.Text(sprintf "Headers (%d)" (List.length headers)) ]
         :: rows)

/// The full response view: a thin status line and a collapsible headers section above the
/// Content-Type-dispatched body. This is what the webview mounts; `renderBody` is exposed
/// separately so the body dispatch can be driven on its own.
let render (env: ResponseEnvelope) : Node =
    el
        "div"
        [ "class", "response" ]
        [ renderStatusLine env
          renderHeaders env.Headers
          el "div" [ "class", "response-body" ] [ renderBody env ] ]
