module Renderer.Core

open System
open System.Text

/// An immutable description of a DOM node. `Element` carries a tag, a flat attribute list
/// (`class`, `src`, `srcdoc`, the boolean `open`, and others), and its children. `Text` is a
/// text node. The webview's `Dom.mount` is the only code that turns this into real
/// `HTMLElement`s.
[<RequireQualifiedAccess>]
type Node =
    | Element of tag: string * attrs: (string * string) list * children: Node list
    | Text of string

/// Blank must not mean "no body", "captured bytes", and "we chose not to read it" at once.
type CapturedBody =
    | NoBody
    | Captured of bytes: byte[]
    | NotCaptured of reason: string

/// The request that was actually sent.
type RequestView =
    { Method: string
      Url: string
      Headers: (string * string) list
      ContentType: string
      Body: CapturedBody }

/// A response that is ready to render. The companion's `ok` envelope supplies `Status`,
/// `Reason`, `Headers`, `ContentType`, the body as bytes, and `RequestMs` (the invocation
/// bracket). `Request` carries the method, URL, headers, and body that the host pairs with it.
/// `TotalMs` is the host's own bracket. The renderer is a pure function of this record, and the
/// Seam-B suite drives canned instances of it.
type ResponseEnvelope =
    { Request: RequestView
      Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      Body: byte[]
      RequestMs: float
      TotalMs: float }

let private el tag attrs children = Node.Element(tag, attrs, children)

let private span cls text =
    Node.Element("span", [ "class", cls ], [ Node.Text text ])

/// Re-encodes a decoded string as a JSON string literal for display. The parser resolves escapes
/// into real characters. A render of a value or a key verbatim between quotes would therefore
/// show an unescaped `"` or `\`, which is dishonest JSON. A second escape restores a literal
/// that reads as JSON. This uses no StringBuilder, and concatenates the chars instead. `Json.fs`
/// states the reason.
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

/// Bins the status into the CSS class that the shell colors on (`status-2xx` to `status-5xx`).
/// The core commits to the class, and not to a hard-coded color, so the shell owns the palette.
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

/// Normalizes a Content-Type for dispatch. It lower-cases the type and removes the parameters,
/// such as `; charset=…`.
let normalizeContentType (contentType: string) : string =
    let ct = if isNull (box contentType) then "" else contentType
    let semi = ct.IndexOf(';')
    let bare = if semi >= 0 then ct.Substring(0, semi) else ct
    bare.Trim().ToLowerInvariant()

let private decodeText (bytes: byte[]) : string = Encoding.UTF8.GetString bytes

let private toBase64 (bytes: byte[]) : string = Convert.ToBase64String bytes

/// A body "looks binary" when it carries a NUL byte, or a large fraction of non-text control
/// bytes. That is the signal that a decode as text gives noise, so the hex fallback wins. This
/// check excludes tab, newline, and carriage return, so ordinary text never reads as binary.
let private looksBinary (bytes: byte[]) : bool =
    if bytes.Length = 0 then
        false
    else
        let isControl b =
            b < 32uy && b <> 9uy && b <> 10uy && b <> 13uy

        let hasNul = bytes |> Array.exists (fun b -> b = 0uy)

        let controlCount = bytes |> Array.filter isControl |> Array.length |> float

        hasNul || controlCount / float bytes.Length > 0.30

let private renderImage (contentType: string) (bytes: byte[]) : Node =
    let mediaType = normalizeContentType contentType
    let src = sprintf "data:%s;base64,%s" mediaType (toBase64 bytes)
    el "img" [ "class", "response-image"; "src", src; "alt", "response image" ] []

let private renderHtml (bytes: byte[]) : Node =
    // An empty `sandbox` denies scripts and same-origin access.
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

/// The note must say "Binary body", because `renderContent` puts this same view inside the
/// Request section, where the bytes are a request body.
let private renderBinary (bytes: byte[]) : Node =
    el
        "div"
        [ "class", "response-binary" ]
        [ el "div" [ "class", "binary-note" ] [ Node.Text(sprintf "Binary body — %s" (humanSize bytes.Length)) ]
          el "pre" [ "class", "hex-dump" ] [ Node.Text(hexDump bytes) ] ]

/// The fallback for text, XML, and unknown types. It gives readable monospace when the bytes
/// decode as text, and the size-and-hex view otherwise. A binary body that does not decode
/// lands here, and never throws.
let private renderTextOrBinary (bytes: byte[]) : Node =
    if looksBinary bytes then
        renderBinary bytes
    else
        renderText bytes

/// A collapsible container arm that the array render and the object render share. It is an open
/// `&lt;details&gt;` whose `&lt;summary&gt;` carries the count label, above the rendered entries.
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
    | None -> renderTextOrBinary bytes

let private isJson (ct: string) =
    ct = "application/json" || ct = "text/json" || ct.EndsWith("+json")

let private isHtml (ct: string) =
    ct = "text/html" || ct = "application/xhtml+xml"

/// Image and HTML previews stay out of this path, because a request body is data that was sent.
let renderContent (contentType: string) (body: byte[]) : Node =
    let ct = normalizeContentType contentType

    if isJson ct then
        renderJson body
    else
        renderTextOrBinary body

/// Dispatches a response body to rendered DOM on its Content-Type. An image goes to an
/// `&lt;img&gt;` with a `data:` URI. JSON goes to a collapsible highlighted tree. HTML goes to a
/// sandboxed iframe. All other content, such as plain text, XML, and unknown types, goes to the
/// monospace-and-hex fallback. This function deliberately does *not* read the status. A non-2xx
/// HTTP error response renders its body exactly like any other, and only the status line shows
/// the code.
let renderBody (env: ResponseEnvelope) : Node =
    let ct = normalizeContentType env.ContentType

    if ct.StartsWith("image/") then
        renderImage env.ContentType env.Body
    elif isHtml ct then
        renderHtml env.Body
    else
        renderContent env.ContentType env.Body

let private renderStatusLine (env: ResponseEnvelope) : Node =
    el
        "div"
        [ "class", "status-line" ]
        [ span "status-method" env.Request.Method
          span "status-url" env.Request.Url
          el
              "span"
              [ "class", sprintf "status-code %s" (statusClass env.Status) ]
              [ Node.Text(sprintf "%d %s" env.Status env.Reason) ]
          span "status-time" (sprintf "%d ms" (int (Math.Round env.RequestMs)))
          span "status-total" (sprintf "· %d ms total" (int (Math.Round env.TotalMs)))
          span "status-size" (humanSize env.Body.Length) ]

let private headerRows (headers: (string * string) list) : Node list =
    headers
    |> List.map (fun (name, value) ->
        el "div" [ "class", "header-row" ] [ span "header-name" name; span "header-value" value ])

let private renderHeaders (headers: (string * string) list) : Node =
    el
        "details"
        [ "class", "headers" ]
        (el "summary" [ "class", "headers-summary" ] [ Node.Text(sprintf "Headers (%d)" (List.length headers)) ]
         :: headerRows headers)

let private requestSummary (body: CapturedBody) : string =
    match body with
    | Captured bytes -> sprintf "Request (%s)" (humanSize bytes.Length)
    | NoBody
    | NotCaptured _ -> "Request"

let private renderRequestBody (request: RequestView) : Node list =
    match request.Body with
    | NoBody -> []
    | NotCaptured reason -> [ el "div" [ "class", "binary-note" ] [ Node.Text reason ] ]
    | Captured bytes -> [ renderContent request.ContentType bytes ]

let private renderRequest (request: RequestView) : Node =
    el
        "details"
        [ "class", "request" ]
        (el "summary" [ "class", "headers-summary" ] [ Node.Text(requestSummary request.Body) ]
         :: headerRows request.Headers
         @ renderRequestBody request)

/// Reads the bytes and never the Content-Type. A JSON body copies the raw bytes, and a binary
/// body copies the same truncated hex dump the panel shows.
let private bodyCopyText (bytes: byte[]) : string =
    if looksBinary bytes then
        hexDump bytes
    else
        decodeText bytes

/// A message-shaped block: a first line, one `Name: value` line for each header, and an optional
/// body after a blank line. The request and the response-headers keys share this shape.
let private messageText (firstLine: string) (headers: (string * string) list) (body: string option) : string =
    let headerLines =
        headers |> List.map (fun (name, value) -> sprintf "%s: %s" name value)

    let head = firstLine :: headerLines |> String.concat "\n"

    match body with
    | None -> head
    | Some text -> head + "\n\n" + text

/// `None` means that there is nothing to copy, and the renderer then omits the button.
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
    | "response-body" ->
        if env.Body.Length = 0 then
            None
        else
            Some(bodyCopyText env.Body)
    | _ -> None

/// The webview's label flash restores exactly this text, so the two ends of that boundary must
/// read one string rather than two copies.
let copyButtonLabel = "Copy"

/// `aria-live` is static markup, so it ships with the button rather than with the label flash
/// that reads it.
let private copyButton (env: ResponseEnvelope) (key: string) : Node list =
    match copyText env key with
    | Some _ ->
        [ el
              "button"
              [ "class", "copy-button"
                "type", "button"
                "data-copy", key
                "aria-live", "polite" ]
              [ Node.Text copyButtonLabel ] ]
    | None -> []

/// Keeps a copy button a sibling of its section, never a descendant of `<details>`, `<summary>`,
/// or a scrolling body.
let private sectionShell (env: ResponseEnvelope) (key: string) (section: Node) : Node =
    el "div" [ "class", "section-shell" ] (copyButton env key @ [ section ])

/// The full response view. It is a thin status line, a collapsible Request section, and a
/// collapsible headers section, above the body that the Content-Type dispatch produced. The
/// webview mounts this view. `renderBody` stays separate, so a caller can drive the body
/// dispatch on its own.
let render (env: ResponseEnvelope) : Node =
    el
        "div"
        [ "class", "response" ]
        [ renderStatusLine env
          sectionShell env "request" (renderRequest env.Request)
          sectionShell env "response-headers" (renderHeaders env.Headers)
          sectionShell env "response-body" (el "div" [ "class", "response-body" ] [ renderBody env ]) ]
