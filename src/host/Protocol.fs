module Protocol

/// The companion process's lifecycle, as the status bar and CodeLens gate see it. Lives here
/// rather than in `Companion.fs` so `statusText` below can take it without a reverse dependency
/// (`Protocol.fs` compiles before `Companion.fs`).
type State =
    | Starting
    | Ready
    | SdkNotFound
    | Stopped

/// What the active editor holds, as the status bar and the no-requests lens see it.
type ScriptView =
    | NoFSharpDocument
    | NotAScript // .fs or .fsi
    | ScriptPending // .fsx, no blocks response yet
    | Script of blocks: int * parseFailed: bool

/// True when a file name names a Script, which is the `.fsx` surface FsHttp.Studio supports.
/// The CodeLens provider and the status bar both decide this, and they must decide it the same
/// way: a lens count on screen and the count the status bar reports come from one `locate`, so a
/// second copy of this test drifting would let one surface treat a buffer as a Script while the
/// other did not.
let isScriptFileName (fileName: string) = fileName.EndsWith ".fsx"

/// `activeFileName` is `None` when the workbench has no active text editor.
/// VSCode asks for lenses on every visible F# document, so a response can arrive for a script the
/// user is not reading.
let mirrorsActiveDocument (activeFileName: string option) (locatedFileName: string) =
    activeFileName = Some locatedFileName

/// The status-bar text for a companion state and a script view, or `None` to hide the item.
/// Companion states other than `Ready` outrank the script view. `NoFSharpDocument` hides the
/// item whatever the companion state is.
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
    | Ready, Script(n, true) when n >= 1 -> Some(sprintf "%d requests: a syntax error can hide others" n)
    // The wire never sends a count below zero, but `int` admits one.
    | Ready, Script(_, false) -> Some "no requests found"
    | Ready, Script(_, true) -> Some "no requests found: syntax error"

/// The CodeLens title for a script that failed to parse and holds no block. `Some` only for
/// `Script(0, true)`. A count at or below
/// zero reads as zero, as it does in `statusText` above.
let noRequestsLensTitle (view: ScriptView) : string option =
    match view with
    | Script(n, true) when n <= 0 -> Some "⊘ No requests found: this script has a syntax error"
    | _ -> None

/// An omitted property decides `false`, so an old companion that never sends it cannot light the
/// syntax-error lens on each response.
let parseFailedOrDefault (value: bool option) : bool = defaultArg value false

/// FCS numbering: 1-based lines, 0-based columns. It duplicates
/// `Companion.BlockLocator.BlockRange` on purpose, because the two sides never share an assembly.
type BlockRange =
    {
        StartLine: int
        StartCol: int
        EndLine: int
        EndCol: int
        /// The block's refusal code from `classify`, or `None` for a block a Run can reach. An
        /// entry that omits the property decodes to `None`. Not yet acted on.
        Refusal: string option
    }

type Diagnostic = { Message: string; Range: BlockRange }

/// Blank must not mean "no body", "captured bytes", and "we chose not to read it" at once.
type CapturedBody =
    | NoBody
    | Captured of bytes: byte[]
    | NotCaptured of reason: string

/// The request that was actually sent, as the companion put it on the `ok` envelope. Mirrors
/// `BlockRunner.RequestData`.
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

/// The response half of a successful Run. Mirrors `BlockRunner.ResponseData`, which exists so
/// that neither side carries a ten-field tuple whose positional call sites are a defect waiting
/// to happen.
type ResponseData =
    { Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      BodyBase64: string
      RequestMs: float }

/// The wire's three-state body triple, exactly as the `request` object spells it. The three
/// fields only ever travel together, and only `capturedBodyFromWire` below reads them.
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
/// below maps this shape onto `RunResult`, so a test can drive it without Fable interop.
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

/// Converts an FCS-native 1-based line to vscode's 0-based line. The columns already agree.
let toVscodeLine (fcsLine: int) : int = fcsLine - 1

/// Shifts the column to 1-based, so the printed `(line,col)` prefix matches vscode's Ln/Col
/// readout.
/// Never an editor diagnostic, because per-block isolation can flag source that is correct in the
/// whole file.
let formatCompileError (diagnostics: Diagnostic list) : string =
    let formatOne (d: Diagnostic) =
        sprintf "(%d,%d) %s" d.Range.StartLine (d.Range.StartCol + 1) d.Message

    let body = diagnostics |> List.map formatOne |> String.concat "\n"
    sprintf "Compile error:\n%s" body

/// Decodes base64 without throwing. The companion writes this field, so a value that will not
/// decode is a defect on our own wire. It reaches `parseRunResult`, which owes its caller a
/// `RunProtocolError` rather than an exception raised inside a promise callback.
let private tryFromBase64 (encoded: string) : byte[] option =
    try
        Some(System.Convert.FromBase64String encoded)
    with _ ->
        None

// The companion and the webview spell these same three names, and share no assembly with this module.
[<Literal>]
let NoneState = "none"

[<Literal>]
let CapturedState = "captured"

[<Literal>]
let NotCapturedState = "notCaptured"

/// An unknown state is a protocol error, because both ends of this wire are ours. Undecodable
/// bytes are reported rather than decayed to `NoBody`, which would claim no body was sent.
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

/// Read back out of the request headers rather than carried on the wire, so it cannot disagree
/// with the header row the same section renders.
/// The lookup is case-insensitive, because a header name is whatever the server or FsHttp wrote.
/// A request with no Content-Type yields `""`, which the renderer treats as an unknown type.
let requestContentType (headers: (string * string) list) : string =
    headers
    |> List.tryFind (fun (name, _) -> name.Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase))
    |> Option.map snd
    |> Option.defaultValue ""

/// Turns a decoded `run` response into a `RunResult`. An `ok` envelope with no `request` object,
/// with an unknown `bodyState`, or with an undecodable captured body, becomes a
/// `RunProtocolError` rather than a crash.
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
