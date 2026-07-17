// A tiny, dependency-free JSON parser for the renderer core. `System.Text.Json` is unavailable
// under Fable and `JS.JSON` is unavailable under .NET, but the renderer core must compile to
// *both* — to JS for the webview and to .NET for the Seam-B Expecto suite. One hand-rolled
// recursive-descent parser over plain F# serves both, and keeps the JSON tree a pure value the
// tests can assert against without a DOM.
module Renderer.Json

// No StringBuilder: Fable 5.9's bundled fable-library omits the zero-arg `StringBuilder.ToString()`
// its own codegen emits, so a StringBuilder here fails to bundle for the webview (caught by running
// the Fable output, not by the .NET suite). Accumulating chars in a ResizeArray sidesteps it and
// bundles cleanly. See the renderer's build smoke.

/// A parsed JSON value. Numbers keep their original source text (`Number`) rather than a float so
/// the tree renders exactly what the server sent — no `1e3`→`1000` or precision drift.
type JsonValue =
    | Null
    | Bool of bool
    | Number of string
    | String of string
    | Array of JsonValue list
    | Object of (string * JsonValue) list

// A minimal cursor over the input. Fable has no `ref`-cell perf concerns here; a mutable index
// kept local to `parse` keeps the recursion straightforward.
type private Cursor = { Text: string; mutable Pos: int }

let private peek (c: Cursor) : char option =
    if c.Pos < c.Text.Length then Some c.Text.[c.Pos] else None

let private isWhitespace ch =
    ch = ' ' || ch = '\t' || ch = '\n' || ch = '\r'

let private skipWhitespace (c: Cursor) =
    while c.Pos < c.Text.Length && isWhitespace c.Text.[c.Pos] do
        c.Pos <- c.Pos + 1

let private fail (c: Cursor) (msg: string) : 'a =
    failwithf "JSON parse error at position %d: %s" c.Pos msg

let private expect (c: Cursor) (ch: char) =
    match peek c with
    | Some actual when actual = ch -> c.Pos <- c.Pos + 1
    | Some actual -> fail c (sprintf "expected '%c' but found '%c'" ch actual)
    | None -> fail c (sprintf "expected '%c' but reached end of input" ch)

/// Consumes a bare literal (`true`/`false`/`null`) once its first character has been matched.
let private literal (c: Cursor) (word: string) =
    if
        c.Pos + word.Length <= c.Text.Length
        && c.Text.Substring(c.Pos, word.Length) = word
    then
        c.Pos <- c.Pos + word.Length
    else
        fail c (sprintf "expected literal '%s'" word)

let private parseHex4 (c: Cursor) : int =
    if c.Pos + 4 > c.Text.Length then
        fail c "truncated \\u escape"

    let slice = c.Text.Substring(c.Pos, 4)
    c.Pos <- c.Pos + 4

    let mutable value = 0

    for ch in slice do
        let digit =
            if ch >= '0' && ch <= '9' then
                int ch - int '0'
            elif ch >= 'a' && ch <= 'f' then
                int ch - int 'a' + 10
            elif ch >= 'A' && ch <= 'F' then
                int ch - int 'A' + 10
            else
                fail c (sprintf "invalid hex digit '%c' in \\u escape" ch)

        value <- value * 16 + digit

    value

let private parseString (c: Cursor) : string =
    expect c '"'
    let chars = ResizeArray<char>()
    let mutable finished = false

    while not finished do
        match peek c with
        | None -> fail c "unterminated string"
        | Some '"' ->
            c.Pos <- c.Pos + 1
            finished <- true
        | Some '\\' ->
            c.Pos <- c.Pos + 1

            match peek c with
            | None -> fail c "unterminated escape"
            | Some esc ->
                c.Pos <- c.Pos + 1

                match esc with
                | '"' -> chars.Add('"')
                | '\\' -> chars.Add('\\')
                | '/' -> chars.Add('/')
                | 'b' -> chars.Add('\b')
                | 'f' -> chars.Add('\f')
                | 'n' -> chars.Add('\n')
                | 'r' -> chars.Add('\r')
                | 't' -> chars.Add('\t')
                | 'u' -> chars.Add(char (parseHex4 c))
                | other -> fail c (sprintf "invalid escape '\\%c'" other)
        | Some ch ->
            c.Pos <- c.Pos + 1
            chars.Add(ch)

    System.String(chars.ToArray())

let private parseNumber (c: Cursor) : JsonValue =
    let start = c.Pos

    let isNumberChar ch =
        (ch >= '0' && ch <= '9')
        || ch = '-'
        || ch = '+'
        || ch = '.'
        || ch = 'e'
        || ch = 'E'

    while c.Pos < c.Text.Length && isNumberChar c.Text.[c.Pos] do
        c.Pos <- c.Pos + 1

    if c.Pos = start then
        fail c "expected a number"

    Number(c.Text.Substring(start, c.Pos - start))

let rec private parseValue (c: Cursor) : JsonValue =
    skipWhitespace c

    match peek c with
    | None -> fail c "unexpected end of input"
    | Some '{' -> parseObject c
    | Some '[' -> parseArray c
    | Some '"' -> String(parseString c)
    | Some 't' ->
        literal c "true"
        Bool true
    | Some 'f' ->
        literal c "false"
        Bool false
    | Some 'n' ->
        literal c "null"
        Null
    | Some ch when ch = '-' || (ch >= '0' && ch <= '9') -> parseNumber c
    | Some ch -> fail c (sprintf "unexpected character '%c'" ch)

and private parseArray (c: Cursor) : JsonValue =
    expect c '['
    skipWhitespace c

    match peek c with
    | Some ']' ->
        c.Pos <- c.Pos + 1
        Array []
    | _ ->
        let items = ResizeArray<JsonValue>()
        let mutable more = true

        while more do
            items.Add(parseValue c)
            skipWhitespace c

            match peek c with
            | Some ',' ->
                c.Pos <- c.Pos + 1
                skipWhitespace c
            | Some ']' ->
                c.Pos <- c.Pos + 1
                more <- false
            | _ -> fail c "expected ',' or ']' in array"

        Array(List.ofSeq items)

and private parseObject (c: Cursor) : JsonValue =
    expect c '{'
    skipWhitespace c

    match peek c with
    | Some '}' ->
        c.Pos <- c.Pos + 1
        Object []
    | _ ->
        let members = ResizeArray<string * JsonValue>()
        let mutable more = true

        while more do
            skipWhitespace c
            let key = parseString c
            skipWhitespace c
            expect c ':'
            let value = parseValue c
            members.Add(key, value)
            skipWhitespace c

            match peek c with
            | Some ',' ->
                c.Pos <- c.Pos + 1
                skipWhitespace c
            | Some '}' ->
                c.Pos <- c.Pos + 1
                more <- false
            | _ -> fail c "expected ',' or '}' in object"

        Object(List.ofSeq members)

/// Parses `text` into a `JsonValue`, returning `None` when it is not valid JSON so the renderer
/// can fall through to the plain-text fallback rather than throwing (an `application/json`
/// Content-Type on a malformed body must still render honestly, not crash the panel).
let tryParse (text: string) : JsonValue option =
    try
        let cursor = { Text = text; Pos = 0 }
        let value = parseValue cursor
        skipWhitespace cursor

        if cursor.Pos = text.Length then Some value else None
    with _ ->
        None
