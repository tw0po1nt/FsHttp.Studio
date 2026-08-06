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

/// The message a pending `run` abandons to when the companion process exits or fails to spawn
/// (docs/spec/0004-run-path-robustness.md, Decision 6). "Reload" is the accurate instruction:
/// nothing restarts the companion today.
let companionStoppedMessage =
    "The FsHttp.Studio companion stopped. Reload the window to start it again."

/// The extension-host mirror of the companion's `run` response tags: `ok`, `compileError`,
/// `runtimeError`, and `refused`. It adds one catch-all case for a malformed or unknown
/// response.
type RunResult =
    | RunOk of status: int * reason: string * headers: (string * string) list * contentType: string * bodyBase64: string
    | RunCompileError of Diagnostic list
    | RunRuntimeError of string
    | RunProtocolError of string
    /// The companion's `classify` refused the target before it evaluated anything. `code` is the
    /// wire spelling (`BlockLocator.codeToWire`). `name` carries the blanked binding's name for
    /// `unboundBlockValue` only.
    | RunRefused of code: string * name: string option

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

/// Reconstructs the exact source text that `r` covers. It mirrors the companion's own
/// `BlockLocator.sliceRange`, so the extension host can read a block's own text for the status
/// line's method and URL, without an extra companion round trip.
let sliceRange (source: string) (r: BlockRange) : string =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    if r.StartLine = r.EndLine then
        lines.[r.StartLine - 1].Substring(r.StartCol, r.EndCol - r.StartCol)
    else
        let parts = ResizeArray<string>()
        parts.Add(lines.[r.StartLine - 1].Substring(r.StartCol))

        for i in r.StartLine .. r.EndLine - 2 do
            parts.Add(lines.[i])

        parts.Add(lines.[r.EndLine - 1].Substring(0, r.EndCol))
        parts |> String.concat "\n"

let private httpVerbs =
    [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH"; "HEAD"; "OPTIONS"; "TRACE" ]

/// A block's own text always starts with a bare HTTP-verb call, such as `GET "url"`. Every
/// companion test fixture uses that shape. A light heuristic over the raw text is therefore
/// enough to read the verb and its URL literal for the status line, without a parse of the CE.
/// Falls back to empty strings, so an unrecognized shape degrades to a blank method and URL
/// instead of a throw. A computed URL or an unusual verb produces such a shape.
let extractMethodAndUrl (blockText: string) : string * string =
    let tokens =
        blockText.Split([| ' '; '\n'; '\r'; '\t'; '{'; '}' |], System.StringSplitOptions.RemoveEmptyEntries)

    match tokens |> Array.tryFind (fun t -> List.contains t httpVerbs) with
    | None -> "", ""
    | Some verb ->
        let searchFrom = blockText.IndexOf(verb) + verb.Length
        let q1 = blockText.IndexOf('"', searchFrom)

        if q1 < 0 then
            verb, ""
        else
            let q2 = blockText.IndexOf('"', q1 + 1)

            if q2 < 0 then
                verb, ""
            else
                verb, blockText.Substring(q1 + 1, q2 - q1 - 1)
