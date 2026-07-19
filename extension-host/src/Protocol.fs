// Pure wire-protocol types and helpers shared by the companion client (`Companion.fs`) and the
// CodeLens/Run wiring. Kept free of Fable/VSCode interop so the string/coordinate
// logic below can be driven directly by a plain .NET test suite (`extension-host/src.Tests`),
// the same seam-isolation the renderer core and companion already use.
module Protocol

/// A source range using FCS's own numbering: 1-based lines, 0-based columns (ADR-0003). Mirrors
/// `Companion.BlockLocator.BlockRange` on the wire — the two sides never share an assembly, so
/// the shape is duplicated deliberately rather than reached across the process boundary.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

type Diagnostic = { Message: string; Range: BlockRange }

/// The extension-host-side mirror of the companion's `run` response tags (`ok` /
/// `compileError` / `runtimeError`), plus a catch-all for a malformed/unknown response.
type RunResult =
    | RunOk of status: int * reason: string * headers: (string * string) list * contentType: string * bodyBase64: string
    | RunCompileError of Diagnostic list
    | RunRuntimeError of string
    | RunProtocolError of string

/// Converts an FCS-native 1-based line to vscode's 0-based line (ADR-0003's coordinate
/// convention); columns already agree between the two, so only the line needs adjusting.
let toVscodeLine (fcsLine: int) : int = fcsLine - 1

/// Formats a Run's compile diagnostics into the response viewer's plain text, prefixing each
/// message with its `(line,col)` source position so it's locatable by reading.
/// The companion's `BlockRange` is 1-based line, 0-based column (ADR-0003); the column is shifted
/// to 1-based here so the printed position matches vscode's own Ln/Col status-bar readout — where
/// the user goes to find it. Deliberately *not* pushed as an editor diagnostic: surfacing why a
/// Run failed is the response viewer's job, and our per-block isolation can flag source that isn't
/// actually wrong in the whole file, so squiggling it would mislead (the editor is Ionide's).
let formatCompileError (diagnostics: Diagnostic list) : string =
    let formatOne (d: Diagnostic) =
        sprintf "(%d,%d) %s" d.Range.StartLine (d.Range.StartCol + 1) d.Message

    let body = diagnostics |> List.map formatOne |> String.concat "\n"
    sprintf "Compile error:\n%s" body

/// Reconstructs the exact source text `r` covers. Mirrors the companion's own
/// `BlockLocator.sliceRange` so the extension host can pull a block's own text (for the status
/// line's method/URL) without an extra companion round trip.
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

/// A block's own text always opens with a bare HTTP-verb call (`GET "url"` — the shape every
/// companion test fixture uses); a light heuristic over the raw text is enough to pull the verb
/// and its URL literal for the status line, without parsing the CE. Falls back to empty strings
/// so an unrecognised shape (a computed URL, an unusual verb) degrades to a blank method/URL
/// rather than throwing.
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
