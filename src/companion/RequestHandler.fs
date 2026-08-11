module Companion.RequestHandler

// Pure request -> response dispatch. It is separate from Program.fs's I/O loop, so a test
// (Seam A) can drive it directly and does not have to spawn the compiled process.

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

// `BlockRunner.outcomeToWire` serializes the outcome, so the host response here and the
// `--worker` child's response emit one identical shape and cannot drift apart.
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
        // Absent `timeoutMs` reads as 0, which means do not inject a bound. That keeps an older
        // host talking to a newer companion honest (docs/spec/0004-run-path-robustness.md,
        // Decision 4).
        let timeoutMs = root |> getIntProp "timeoutMs"
        runResponse source blockIndex scriptFileName timeoutMs
    | other ->
        {| tag = "error"
           message = sprintf "unknown request tag '%s'" (string other) |}
