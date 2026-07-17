module Companion.RequestHandler

// Pure request -> response dispatch, factored out of Program.fs's I/O loop so it can be
// driven directly in tests (Seam A) without spawning the compiled process.

open System.Text.Json
open Companion.BlockLocator

let private getStringProp (name: string) (root: JsonElement) =
    match root.TryGetProperty name with
    | true, v -> v.GetString()
    | false, _ -> null

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

        let ranges =
            locate source
            |> List.map (fun r ->
                {| startLine = r.StartLine
                   startCol = r.StartCol
                   endLine = r.EndLine
                   endCol = r.EndCol |})

        {| tag = "blocks"; ranges = ranges |}
    | other ->
        {| tag = "error"
           message = sprintf "unknown request tag '%s'" (string other) |}
