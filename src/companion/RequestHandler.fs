module Companion.RequestHandler


open System.Reflection
open System.Text.Json
open Companion.Envelope
open Companion.BlockLocator
open Companion.BlockRunner

/// Typed `obj` because a refused entry carries `refusal` and a supported entry omits the property,
/// which makes the two branches differently-shaped anonymous records.
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

/// The build sets the informational version to the package version, with no source revision.
let companionVersion =
    match Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
    | null -> None
    | attribute -> Some attribute.InformationalVersion

let ready (version: string option) : obj =
    match version with
    | Some version -> {| tag = "ready"; version = version |}
    | None -> {| tag = "ready" |}

/// Handles one decoded request payload. Returns the response object that the caller
/// serializes onto the frame channel.
let respond (request: JsonDocument) : obj =
    let root = request.RootElement
    let tag = root |> getStringProp "tag"

    match tag with
    | "hello" -> ready companionVersion
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
