// Pure wire-protocol types and helpers, shared by the companion client (`Companion.fs`) and the
// CodeLens and Run wiring. This module carries no Fable or VSCode interop, so a plain .NET test
// suite (`tests/host.Tests`) can drive the string and coordinate logic below directly. The
// renderer core and the companion already use the same seam isolation.
module Protocol

/// The companion process's lifecycle, as the status bar and CodeLens gate see it. Lives here
/// rather than in `Companion.fs` so `statusText` below can take it without a reverse dependency
/// (`Protocol.fs` compiles before `Companion.fs`).
type State =
    | Starting
    | Ready
    | SdkNotFound
    | Stopped

/// What the active editor holds, as the status bar and the no-requests lens see it
/// (docs/spec/0014-explain-missing-lenses.md, Decision 5).
type ScriptView =
    | NoFSharpDocument
    | NotAScript // .fs or .fsi
    | ScriptPending // .fsx, no blocks response yet
    | Script of blocks: int * parseFailed: bool

/// The status-bar text for a companion state and a script view, or `None` to hide the item.
/// Companion states other than `Ready` outrank the script view. `NoFSharpDocument` hides the
/// item whatever the companion state is (docs/spec/0014-explain-missing-lenses.md, Decisions 5-6).
let statusText (state: State) (view: ScriptView) : string option =
    match state, view with
    | _, NoFSharpDocument -> None
    | Starting, _ -> Some "starting…"
    | SdkNotFound, _ -> Some ".NET SDK not found"
    | Stopped, _ -> Some "companion stopped"
    | Ready, NotAScript -> Some "not an .fsx script"
    | Ready, ScriptPending -> Some "looking for requests…"
    | Ready, Script(1, false) -> Some "1 request"
    | Ready, Script(n, false) when n > 1 -> Some(sprintf "%d requests" n)
    | Ready, Script(n, true) when n >= 1 -> Some(sprintf "%d requests — a syntax error can hide others" n)
    // A count at or below zero reads as "none found". `int` admits a negative the wire never
    // sends; folding it in here keeps `None` meaning only "hide the item" (Decision 6).
    | Ready, Script(_, false) -> Some "no requests found"
    | Ready, Script(_, true) -> Some "no requests found — syntax error"

/// The CodeLens title for a script that failed to parse and holds no block. `Some` only for
/// `Script(0, true)` (docs/spec/0014-explain-missing-lenses.md, Decision 2). A count at or below
/// zero reads as zero, as it does in `statusText` above.
let noRequestsLensTitle (view: ScriptView) : string option =
    match view with
    | Script(n, true) when n <= 0 -> Some "⊘ No requests found: this script has a syntax error"
    | _ -> None

/// A source range in FCS's own numbering: 1-based lines, 0-based columns (ADR-0003). It mirrors
/// `Companion.BlockLocator.BlockRange` on the wire. The two sides never share an assembly, so
/// this duplicate shape is deliberate, and neither side reaches across the process boundary.
type BlockRange =
    {
        StartLine: int
        StartCol: int
        EndLine: int
        EndCol: int
        /// The block's refusal code from `classify`, spelled as Decision 2 spells it, or `None`
        /// for a block a Run can reach. An entry that omits the property decodes to `None`
        /// (docs/spec/0003-lens-tells-the-truth.md, Decision 4). Not yet acted on.
        Refusal: string option
    }

type Diagnostic = { Message: string; Range: BlockRange }

/// A request body as the viewer must see it. Blank must not mean "no body", "captured bytes",
/// and "we chose not to read it" at once (docs/spec/0012-request-as-sent.md, Decision 8).
type CapturedBody =
    | NoBody
    | Captured of bytes: byte[]
    | NotCaptured of reason: string

/// The request that was actually sent, as the companion put it on the `ok` envelope. Mirrors
/// `BlockRunner.RequestData` (docs/spec/0012-request-as-sent.md, Decisions 9-10).
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

/// The response half of a successful Run. Mirrors `BlockRunner.ResponseData`, which exists so
/// that neither side carries a ten-field tuple whose positional call sites are a defect waiting
/// to happen (docs/spec/0012-request-as-sent.md, Decision 9).
type ResponseData =
    { Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      BodyBase64: string
      RequestMs: float }

/// The wire's three-state body triple, exactly as the `request` object spells it. The three
/// fields only ever travel together, and only `capturedBodyFromWire` below reads them
/// (docs/spec/0012-request-as-sent.md, Decision 10).
type WireBody =
    { State: string
      Base64: string
      Reason: string }

/// The nested `request` object on an `ok` envelope. The JS side fills this in after it reads the
/// properties; `requestFromWire` turns it into `RequestData`.
type WireRequest =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: WireBody }

/// A `run` response after the JS side has read its properties, named for the glossary's
/// **Envelope** rather than for `Envelope.fs`'s length-prefixed transport frame. The pure parse
/// below maps this shape onto `RunResult`, so Seam 3 can drive it without Fable interop.
///
/// On `OkEnvelope`, a `request` of `None` means the property was absent, which is a protocol
/// error rather than a crash.
type RunEnvelope =
    | OkEnvelope of response: ResponseData * request: WireRequest option
    | CompileErrorEnvelope of Diagnostic list
    | RuntimeErrorEnvelope of string
    | RefusedEnvelope of code: string * name: string option
    | ProtocolErrorEnvelope of string

/// The extension-host mirror of the companion's `run` response tags: `ok`, `compileError`,
/// `runtimeError`, and `refused`. It adds one catch-all case for a malformed or unknown
/// response.
type RunResult =
    | RunOk of request: RequestData * response: ResponseData
    | RunCompileError of Diagnostic list
    | RunRuntimeError of string
    | RunProtocolError of string
    /// The companion's `classify` refused the target before it evaluated anything. `code` is the
    /// wire spelling (`BlockLocator.codeToWire`). `name` carries the blanked binding's name for
    /// `unboundBlockValue` only.
    | RunRefused of code: string * name: string option

/// The `scriptFileName` a Run sends for a script with this URI scheme and `fileName`.
///
/// FSI sets `__SOURCE_DIRECTORY__` and `__SOURCE_FILE__` from the path, so the path must be a
/// real local one. Only the `file` scheme guarantees that. An untitled buffer (`untitled`), a
/// virtual or remote workspace (`vscode-vfs`, and the remote providers), and a diff view
/// (`git`) all carry a `fileName` that no local read can resolve, so a Run must send nothing
/// rather than invent a directory for them. `None` keeps FSI's own default.
let scriptFileNameFor (scheme: string) (fileName: string) : string option =
    if scheme = "file" then Some fileName else None

/// Converts an FCS-native 1-based line to vscode's 0-based line (ADR-0003's coordinate
/// convention). The columns already agree, so only the line needs an adjustment.
let toVscodeLine (fcsLine: int) : int = fcsLine - 1

/// Formats a Run's compile diagnostics into the response viewer's plain text. Each message
/// carries its `(line,col)` source position as a prefix, so a reader can locate it.
///
/// The companion's `BlockRange` is a 1-based line and a 0-based column (ADR-0003). This
/// function shifts the column to 1-based, so the printed position matches vscode's own Ln/Col
/// status-bar readout, which is where the user looks for it.
///
/// This text is deliberately *not* an editor diagnostic. The response viewer owns the report of
/// why a Run failed. Our per-block isolation can also flag source that is not wrong in the
/// whole file, so an editor squiggle would mislead the user. Ionide owns the editor.
let formatCompileError (diagnostics: Diagnostic list) : string =
    let formatOne (d: Diagnostic) =
        sprintf "(%d,%d) %s" d.Range.StartLine (d.Range.StartCol + 1) d.Message

    let body = diagnostics |> List.map formatOne |> String.concat "\n"
    sprintf "Compile error:\n%s" body

/// Decodes base64 without throwing. The companion writes this field, so a value that will not
/// decode is a defect on our own wire — but it reaches `parseRunResult`, which owes its caller a
/// `RunProtocolError` and not an exception raised inside a promise callback.
let private tryFromBase64 (encoded: string) : byte[] option =
    try
        Some(System.Convert.FromBase64String encoded)
    with _ ->
        None

// The three `bodyState` names of Decision 10, written once for the host. `capturedBodyFromWire`
// reads them off the companion's envelope and `RunCommand` writes the same three onto the viewer
// update, so a name spelled separately at each end could drift by a letter and a state would be
// lost between the two. The companion and the webview each spell them once too — neither shares
// an assembly with this one.
[<Literal>]
let NoneState = "none"

[<Literal>]
let CapturedState = "captured"

[<Literal>]
let NotCapturedState = "notCaptured"

/// Maps the wire's three-state body triple onto `CapturedBody`
/// (docs/spec/0012-request-as-sent.md, Decision 10). An unknown state is a protocol error: both
/// ends of this wire are ours, so a bad value is a defect. Undecodable bytes are the same kind of
/// defect, and are reported rather than decayed to `NoBody`, which would tell the reader no body
/// was sent when one was.
let private capturedBodyFromWire (body: WireBody) : Result<CapturedBody, string> =
    match body.State with
    | NoneState -> Ok NoBody
    | CapturedState ->
        match tryFromBase64 body.Base64 with
        | Some bytes -> Ok(Captured bytes)
        | None -> Error "ok envelope has a captured request body that is not valid base64"
    | NotCapturedState -> Ok(NotCaptured body.Reason)
    | other -> Error(sprintf "ok envelope has unknown bodyState '%s'" other)

/// Turns the wire's `request` object into the `RequestData` the viewer reads. Lives beside
/// `WireRequest`, so the field-by-field mapping is not spread through the parse below.
let private requestFromWire (request: WireRequest) : Result<RequestData, string> =
    capturedBodyFromWire request.Body
    |> Result.map (fun body ->
        { Method = request.Method
          Url = request.Url
          Headers = request.Headers
          Body = body })

/// The Content-Type the Request section dispatches a request body on. It is read back out of the
/// request headers the companion already collected, rather than carried as a fourth field on the
/// wire: `RequestData` has no `ContentType` (Decision 9), and one derived here cannot disagree
/// with the header row the same section renders.
///
/// The lookup is case-insensitive, because a header name on the wire is whatever the server or
/// FsHttp wrote. A request with no Content-Type — a `GET`, most often — yields `""`, which the
/// renderer's dispatch already treats as an unknown type.
///
/// Lives here rather than in `RunCommand`, which is Fable and VSCode interop with no suite of its
/// own (docs/spec/0012-request-as-sent.md, Seam 3).
let requestContentType (headers: (string * string) list) : string =
    headers
    |> List.tryFind (fun (name, _) -> name.Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase))
    |> Option.map snd
    |> Option.defaultValue ""

/// Turns a decoded `run` response into a `RunResult`. An `ok` envelope with no `request` object,
/// with an unknown `bodyState`, or with an undecodable captured body, is a `RunProtocolError` and
/// not a crash (docs/spec/0012-request-as-sent.md, Seam 3).
let parseRunResult (envelope: RunEnvelope) : RunResult =
    match envelope with
    | CompileErrorEnvelope diagnostics -> RunCompileError diagnostics
    | RuntimeErrorEnvelope message -> RunRuntimeError message
    | RefusedEnvelope(code, name) -> RunRefused(code, name)
    | ProtocolErrorEnvelope message -> RunProtocolError message
    | OkEnvelope(_, None) -> RunProtocolError "ok envelope is missing the request object"
    | OkEnvelope(response, Some request) ->
        match requestFromWire request with
        | Error message -> RunProtocolError message
        | Ok requestData -> RunOk(requestData, response)
