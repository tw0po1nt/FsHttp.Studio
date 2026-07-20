module Companion.RequestHandler

// Pure request -> response dispatch, factored out of Program.fs's I/O loop so it can be
// driven directly in tests (Seam A) without spawning the compiled process.

open System.Text.Json
open Companion.Envelope
open Companion.BlockLocator
open Companion.BlockRunner

let private toRangeObj (r: BlockRange) =
    {| startLine = r.StartLine
       startCol = r.StartCol
       endLine = r.EndLine
       endCol = r.EndCol |}

// Serializing the outcome lives in `BlockRunner.outcomeToWire` so the host response here and the
// `--worker` child's response emit one identical shape and can't drift.
let private runResponse (source: string) (blockIndex: int) : obj = outcomeToWire (run source blockIndex)

/// Handles one decoded request payload and returns the response object to serialize onto
/// the frame channel.
let respond (request: JsonDocument) : obj =
    let root = request.RootElement
    let tag = root |> getStringProp "tag"

    match tag with
    | "hello" -> {| tag = "ready" |}
    | "locate" ->
        let source = root |> getStringProp "source"
        let ranges = locate source |> List.map toRangeObj
        {| tag = "blocks"; ranges = ranges |}
    | "run" ->
        let source = root |> getStringProp "source"
        let blockIndex = root |> getIntProp "blockIndex"
        runResponse source blockIndex
    | other ->
        {| tag = "error"
           message = sprintf "unknown request tag '%s'" (string other) |}
