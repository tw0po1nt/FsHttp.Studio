// Pure wire-protocol types and helpers, shared by the companion client (`Companion.fs`) and the
// CodeLens and Run wiring. This module carries no Fable or VSCode interop, so a plain .NET test
// suite (`tests/host.Tests`) can drive the string and coordinate logic below directly. The
// renderer core and the companion already use the same seam isolation.
module Protocol

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

/// The request that was actually sent, as the companion put it on the `ok` frame
/// (docs/spec/0012-request-as-sent.md, Decisions 9-10).
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

/// Wire fields for the nested `request` object on an `ok` frame. The JS side fills these after
/// it reads the properties. `parseRunResult` turns them into `RequestData`.
type OkRequestFields =
    { Method: string
      Url: string
      Headers: (string * string) list
      BodyState: string
      BodyBase64: string
      BodyReason: string }

/// An `ok` frame's top-level fields, plus an optional nested request. `Request = None` means
/// the property was absent, which is a protocol error rather than a crash.
type OkFields =
    { Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      BodyBase64: string
      RequestMs: float
      Request: OkRequestFields option }

/// A `run` response after the JS side has read its properties. The pure parse below maps this
/// shape onto `RunResult`, so Seam 3 can drive it without Fable interop.
type RunFrame =
    | OkFrame of OkFields
    | CompileErrorFrame of Diagnostic list
    | RuntimeErrorFrame of string
    | RefusedFrame of code: string * name: string option
    | ProtocolErrorFrame of string

/// The extension-host mirror of the companion's `run` response tags: `ok`, `compileError`,
/// `runtimeError`, and `refused`. It adds one catch-all case for a malformed or unknown
/// response.
type RunResult =
    | RunOk of
        request: RequestData *
        status: int *
        reason: string *
        headers: (string * string) list *
        contentType: string *
        bodyBase64: string *
        requestMs: float
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

/// Maps the wire's three-state `bodyState` / `bodyBase64` / `bodyReason` triple onto
/// `CapturedBody` (docs/spec/0012-request-as-sent.md, Decision 10). An unknown state is a
/// protocol error: both ends of this wire are ours, so a bad value is a defect.
let private capturedBodyFromWire
    (bodyState: string)
    (bodyBase64: string)
    (bodyReason: string)
    : Result<CapturedBody, string> =
    match bodyState with
    | "none" -> Ok NoBody
    | "captured" -> Ok(Captured(System.Convert.FromBase64String bodyBase64))
    | "notCaptured" -> Ok(NotCaptured bodyReason)
    | other -> Error(sprintf "ok frame has unknown bodyState '%s'" other)

/// Turns a decoded `run` response into a `RunResult`. An `ok` frame with no `request` object,
/// or with an unknown `bodyState`, is a `RunProtocolError` and not a crash
/// (docs/spec/0012-request-as-sent.md, Seam 3).
let parseRunResult (frame: RunFrame) : RunResult =
    match frame with
    | CompileErrorFrame diagnostics -> RunCompileError diagnostics
    | RuntimeErrorFrame message -> RunRuntimeError message
    | RefusedFrame(code, name) -> RunRefused(code, name)
    | ProtocolErrorFrame message -> RunProtocolError message
    | OkFrame fields ->
        match fields.Request with
        | None -> RunProtocolError "ok frame is missing the request object"
        | Some req ->
            match capturedBodyFromWire req.BodyState req.BodyBase64 req.BodyReason with
            | Error message -> RunProtocolError message
            | Ok body ->
                RunOk(
                    { Method = req.Method
                      Url = req.Url
                      Headers = req.Headers
                      Body = body },
                    fields.Status,
                    fields.Reason,
                    fields.Headers,
                    fields.ContentType,
                    fields.BodyBase64,
                    fields.RequestMs
                )
