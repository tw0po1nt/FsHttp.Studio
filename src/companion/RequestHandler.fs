module Companion.RequestHandler

// Pure request -> response dispatch. It is separate from Program.fs's I/O loop, so a test
// (Seam A) can drive it directly and does not have to spawn the compiled process.

open System.Text.Json
open Companion.Envelope
open Companion.BlockLocator
open Companion.BlockRunner

let private toRangeObj (r: BlockRange) =
    {| startLine = r.StartLine
       startCol = r.StartCol
       endLine = r.EndLine
       endCol = r.EndCol |}

// `BlockRunner.outcomeToWire` serializes the outcome, so the host response here and the
// `--worker` child's response emit one identical shape and cannot drift apart.
let private runResponse (source: string) (blockIndex: int) : obj = outcomeToWire (run source blockIndex)

/// Handles one decoded request payload. Returns the response object that the caller
/// serializes onto the frame channel.
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
