module Companion.RequestHandler


open System.Text.Json
open Companion.Envelope
open Companion.BlockLocator
open Companion.BlockRunner

/// A block's wire entry in the `locate` response. A refused block's entry carries its refusal
/// code; a supported block's entry omits the `refusal` property entirely, so the two branches
/// return two differently-shaped anonymous records and the entry is typed `obj`
/// (docs/spec/0003-lens-tells-the-truth.md, Decision 4). The coordinates are written once, and
/// the refused branch copies-and-extends them, so a later coordinate field cannot reach one
/// branch and miss the other.
let private toBlockEntry (block: LocatedBlock) : obj =
    let r = block.Block

    let coords =
        {| startLine = r.StartLine
           startCol = r.StartCol
           endLine = r.EndLine
           endCol = r.EndCol |}

    match refusalOf block.Route with
    | Some refusal -> {| coords with refusal = refusal |} :> obj
    | None -> coords :> obj

// This response and the `--worker` child's response must stay one identical shape.
let private runResponse (source: string) (blockIndex: int) (scriptFileName: string option) (timeoutMs: int) : obj =
    outcomeToWire (run source blockIndex scriptFileName timeoutMs)

/// Handles one decoded request payload. Returns the response object that the caller
/// serializes onto the frame channel.
let respond (request: JsonDocument) : obj =
    let root = request.RootElement
    let tag = root |> getStringProp "tag"

    match tag with
    | "hello" -> {| tag = "ready" |}
    | "locate" ->
        let source = root |> getStringProp "source"
        let located = locateBlocks source
        let ranges = located.Blocks |> List.map toBlockEntry

        {| tag = "blocks"
           parseFailed = located.ParseFailed
           ranges = ranges |}
    | "run" ->
        let source = root |> getStringProp "source"
        let blockIndex = root |> getIntProp "blockIndex"
        let scriptFileName = root |> getOptionalStringProp "scriptFileName"
        // An absent `timeoutMs` reads as 0, which means inject no bound.
        let timeoutMs = root |> getIntProp "timeoutMs"
        runResponse source blockIndex scriptFileName timeoutMs
    | other ->
        {| tag = "error"
           message = sprintf "unknown request tag '%s'" (string other) |}
