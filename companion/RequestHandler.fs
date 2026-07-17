module Companion.RequestHandler

// Pure request -> response dispatch, factored out of Program.fs's I/O loop so it can be
// driven directly in tests (Seam A) without spawning the compiled process.

open System.Text.Json
open Companion.BlockLocator
open Companion.BlockRunner

let private getStringProp (name: string) (root: JsonElement) =
    match root.TryGetProperty name with
    | true, v -> v.GetString()
    | false, _ -> null

let private getIntProp (name: string) (root: JsonElement) =
    match root.TryGetProperty name with
    | true, v -> v.GetInt32()
    | false, _ -> 0

let private toRangeObj (r: BlockRange) =
    {| startLine = r.StartLine
       startCol = r.StartCol
       endLine = r.EndLine
       endCol = r.EndCol |}

let private runResponse (source: string) (blockIndex: int) : obj =
    match run source blockIndex with
    | Ok(status, reason, headers, contentType, bodyBase64) ->
        {| tag = "ok"
           status = status
           reason = reason
           headers = dict headers
           contentType = contentType
           bodyBase64 = bodyBase64 |}
    | CompileError diagnostics ->
        {| tag = "compileError"
           diagnostics =
            diagnostics
            |> List.map (fun d ->
                {| message = d.Message
                   range = toRangeObj d.Range |}) |}
    | RuntimeError message ->
        {| tag = "runtimeError"
           message = message |}

/// Handles one decoded request payload and returns the response object to serialize onto
/// the frame channel.
let respond (request: JsonDocument) : obj =
    let root = request.RootElement
    let tag = root |> getStringProp "tag"

    match tag with
    | "hello" -> {| tag = "ready" |}
    | "locate" ->
        let source =
            root |> getStringProp "source" |> Option.ofObj |> Option.defaultValue ""

        let ranges = locate source |> List.map toRangeObj
        {| tag = "blocks"; ranges = ranges |}
    | "run" ->
        let source =
            root |> getStringProp "source" |> Option.ofObj |> Option.defaultValue ""

        let blockIndex = root |> getIntProp "blockIndex"
        runResponse source blockIndex
    | other ->
        {| tag = "error"
           message = sprintf "unknown request tag '%s'" (string other) |}
